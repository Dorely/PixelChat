import { createHash } from 'node:crypto';
import workflow from '../../resources/skill/references/workflow.md?raw';
import library from '../../resources/skill/references/library.md?raw';
import imageEditing from '../../resources/skill/references/image-editing.md?raw';

/** Canonical skill references: the app, the CLI build, and the installer all embed these exact texts. */
export const guidanceDocuments: Record<string, string> = { 'workflow.md': workflow, 'library.md': library, 'image-editing.md': imageEditing };

/** Hash of the complete manifest; installed skills record it in guidance.json to detect stale copies. */
export const guidanceHash = () => createHash('sha256').update(JSON.stringify(guidanceDocuments)).digest('hex');

export function selectGuidance(topics?: string[]) {
  if (!topics) return guidanceDocuments;
  return Object.fromEntries(topics.map(topic => {
    const name = topic.endsWith('.md') ? topic : `${topic}.md`;
    if (!Object.hasOwn(guidanceDocuments, name)) throw new Error(`Unknown guidance topic: ${topic}. Available: ${Object.keys(guidanceDocuments).join(', ')}`);
    return [name, guidanceDocuments[name]];
  }));
}
