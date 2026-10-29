import { randomUUID } from 'node:crypto';
import { now, type Store } from './store';
import type { ChatAttachment, Message, MessageRole, MessageStatus } from '../shared/types';

type Row = Record<string, any>;
const message = (r: Row): Message => ({ id: r.id, sessionId: r.session_id, role: r.role, text: r.text, status: r.status, attachments: JSON.parse(r.attachments), createdAt: r.created_at });
const TITLE_MAX = 70;

/**
 * Conversation persistence for the inline assistant: visible messages, the provider input history,
 * and the tool-call ledger that keeps an interrupted call from being replayed.
 */
export class ChatStore {
  constructor(private store: Store) {}
  private get db() { return this.store.db; }

  messages(sessionId: string): Message[] { return (this.db.prepare('SELECT * FROM messages WHERE session_id=? ORDER BY rowid').all(sessionId) as Row[]).map(message); }
  addMessage(sessionId: string, role: MessageRole, text: string, status: MessageStatus = 'completed', attachments: ChatAttachment[] = []): Message {
    const entry: Message = { id: randomUUID(), sessionId, role, text, status, attachments, createdAt: now() };
    this.db.prepare('INSERT INTO messages (id,session_id,role,text,status,attachments,created_at) VALUES (?,?,?,?,?,?,?)').run(entry.id, sessionId, role, text, status, JSON.stringify(attachments), entry.createdAt);
    if (role === 'user') this.db.prepare('UPDATE sessions SET updated_at=? WHERE id=?').run(entry.createdAt, sessionId);
    this.store.changed(); return entry;
  }
  updateMessage(entry: Message) { this.db.prepare('UPDATE messages SET text=?, status=?, attachments=? WHERE id=?').run(entry.text, entry.status, JSON.stringify(entry.attachments), entry.id); this.store.changed(); }
  /** Names an automatically titled conversation after its first message; user and assistant titles stay. */
  autoTitle(sessionId: string, text: string) {
    const title = text.replace(/\s+/g, ' ').trim().slice(0, TITLE_MAX).trim();
    if (title) this.db.prepare("UPDATE sessions SET title=? WHERE id=? AND title_source='auto' AND NOT EXISTS (SELECT 1 FROM messages WHERE session_id=? AND role='user')").run(title, sessionId, sessionId);
  }

  history(sessionId: string): unknown[] { return JSON.parse((this.db.prepare('SELECT input FROM chat_history WHERE session_id=?').get(sessionId) as Row | undefined)?.input ?? '[]'); }
  setHistory(sessionId: string, input: unknown[]) { this.db.prepare('INSERT INTO chat_history (session_id,input) VALUES (?,?) ON CONFLICT(session_id) DO UPDATE SET input=excluded.input').run(sessionId, JSON.stringify(input)); }

  /** Records a tool call before it runs. A repeated call id returns the recorded outcome instead of running again. */
  beginTool(sessionId: string, id: string, name: string, args: string): { fresh: boolean; result: string | null } {
    const existing = this.db.prepare('SELECT * FROM tool_calls WHERE session_id=? AND id=?').get(sessionId, id) as Row | undefined;
    if (existing) {
      if (existing.name !== name || existing.args !== args) throw new Error('Conflicting duplicate tool call.');
      return { fresh: false, result: existing.result };
    }
    this.db.prepare("INSERT INTO tool_calls (session_id,id,name,args,status,result) VALUES (?,?,?,?,'running',NULL)").run(sessionId, id, name, args);
    return { fresh: true, result: null };
  }
  /** A billed request was admitted under this id; recovery reports it instead of replaying it. */
  waitTool(sessionId: string, id: string, result: unknown) { this.db.prepare("UPDATE tool_calls SET status='waiting', result=? WHERE session_id=? AND id=?").run(JSON.stringify(result), sessionId, id); }
  finishTool(sessionId: string, id: string, result: string) { this.db.prepare("UPDATE tool_calls SET status='completed', result=? WHERE session_id=? AND id=?").run(result, sessionId, id); }

  /**
   * Restores function calls whose outputs survived but whose call items did not, from the ledger.
   * Providers reject an output without its call; anything the ledger cannot vouch for fails loudly.
   */
  repairHistory(sessionId: string, input: unknown[]): { input: unknown[]; restoredCallIds: string[] } {
    const repaired: unknown[] = [], restoredCallIds: string[] = [];
    const seenCalls = new Set<string>(), seenOutputs = new Set<string>();
    const ledger = this.db.prepare('SELECT name,args,status,result FROM tool_calls WHERE session_id=? AND id=?');
    const fail = (detail: string) => { throw new Error(`Cannot repair the conversation history: ${detail}`); };
    for (const item of input) {
      const record = item && typeof item === 'object' ? item as Record<string, unknown> : null;
      if (record?.type === 'function_call' || record?.type === 'function_call_output') {
        const callId = record.call_id;
        if (typeof callId !== 'string' || !callId) fail(`${record.type} is missing call_id.`);
        const seen = record.type === 'function_call' ? seenCalls : seenOutputs;
        if (seen.has(callId as string)) fail(`duplicate ${record.type} ${callId}.`);
        seen.add(callId as string);
        if (record.type === 'function_call_output' && !seenCalls.has(callId as string)) {
          const row = ledger.get(sessionId, callId) as Row | undefined;
          if (!row || row.status !== 'completed' || row.result === null) fail(`tool call ${callId} has no completed ledger record.`);
          repaired.push({ type: 'function_call', call_id: callId, name: row!.name, arguments: row!.args });
          restoredCallIds.push(callId as string); seenCalls.add(callId as string);
        }
      }
      repaired.push(item);
    }
    return { input: repaired, restoredCallIds };
  }

  /** Startup recovery: interrupted replies are marked failed and in-flight tool calls uncertain, never replayed. */
  interrupt() {
    this.store.tx(() => {
      this.db.prepare("UPDATE messages SET status='failed', text=text || ? WHERE status='running'").run('\n[Interrupted when PixelChat closed.]');
      this.db.prepare("UPDATE tool_calls SET status='uncertain', result=? WHERE status='running'").run(JSON.stringify({ error: 'Interrupted when PixelChat closed; the outcome is unknown and the call was not replayed.' }));
      for (const row of this.db.prepare("SELECT session_id,id,result FROM tool_calls WHERE status='waiting'").all() as Row[]) {
        this.db.prepare("UPDATE tool_calls SET status='uncertain', result=? WHERE session_id=? AND id=?")
          .run(JSON.stringify({ ...JSON.parse(row.result), interrupted: true, message: 'Interrupted after admission. Inspect jobs_get, or repeat with the same requestId to recover the original jobs; this call was not replayed.' }), row.session_id, row.id);
      }
    });
  }
}
