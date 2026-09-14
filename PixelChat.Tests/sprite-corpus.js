// Deterministic engineering corpus. The harness supplies kind and the native document snapshot.
const base = document.frames[0].id;
const body = document.layers[0].id;
const detail = body.slice(0, 24) + '00000000ffff';
sprite.apply({op:'addLayer',id:detail,name:'Details'});
const count = kind === 'prop' ? 1 : 8;
const frames = [];
for (let i = 0; i < count; i++) {
    const frameId = i === 0 ? base : base.slice(0,24) + String(i).padStart(12,'0');
    frames.push(frameId);
    if (i) sprite.apply({op:'addFrame',id:frameId,name:'Pose '+(i+1),durationMs:100});
    const draw = (op, values, layerId=body) => sprite.apply({op,frameId,layerId,...values});
    const rect = (x,y,width,height,color,layer=body) => draw('rectangle',{x,y,width,height,color,filled:true},layer);
    const oval = (x,y,width,height,color,layer=body) => draw('ellipse',{x,y,width,height,color,filled:true},layer);
    const line = (x,y,x2,y2,color,size=1,layer=body) => draw('line',{x,y,x2,y2,color,size},layer);
    const phase = i*Math.PI/4;
    if (kind === 'prop') {
        rect(19,46,26,5,'#172840ff');
        for (let y=12;y<46;y++) { const half=Math.floor(12*(1-Math.abs(y-27)/18)); if(half>0) rect(32-half,y,half*2,1,'#198dabff'); }
        line(32,12,25,29,'#a0f3ffff',2,detail); line(25,29,32,45,'#4fcbdbff',2,detail);
        line(32,12,39,29,'#246386ff',2,detail); line(39,29,32,45,'#246386ff',2,detail);
    } else if (kind === 'walk') {
        const swing=Math.round(Math.sin(phase)*7), bob=i%4===1?1:0;
        line(28,39+bob,26-swing,51,'#26354eff',5); line(36,39+bob,36+swing,51,'#586683ff',5);
        rect(22-swing,51,9,3,'#172840ff'); rect(33+swing,51,9,3,'#172840ff');
        rect(24,24+bob,16,18,'#318c87ff'); rect(26,27+bob,13,4,'#57b9a0ff',detail);
        oval(25,10+bob,17,17,'#e8b184ff'); rect(25,10+bob,16,5,'#3e2b39ff',detail);
        rect(39,18+bob,5,4,'#e8b184ff'); rect(37,17+bob,2,2,'#172840ff',detail);
        line(27,28+bob,30+swing,39,'#e8b184ff',4,detail);
        sprite.apply({op:'setProvenance',key:'phase:'+frameId,value:['left contact','down','passing','up','right contact','down','passing','up'][i]});
    } else if (kind === 'idle') {
        const squash=Math.round(Math.sin(phase)*3);
        oval(15-squash,28+squash,34+squash*2,23-squash,'#336d83ff');
        oval(20-squash,29+squash,23+squash,13-squash,'#5db9a6ff',detail);
        oval(24,32+squash,4,5,'#e4f3caff',detail); oval(36,32+squash,4,5,'#e4f3caff',detail);
        rect(26,34+squash,2,3,'#172840ff',detail); rect(38,34+squash,2,3,'#172840ff',detail);
    } else if (kind === 'impact') {
        const radius=4+i*3;
        for(let ray=0;ray<8;ray++) {const a=ray*Math.PI/4;line(32+Math.round(Math.cos(a)*radius*.5),32+Math.round(Math.sin(a)*radius*.5),32+Math.round(Math.cos(a)*radius),32+Math.round(Math.sin(a)*radius),'#ef7952ff',Math.max(1,4-i/2|0));}
        if(i<5) oval(28-i,28-i,8+i*2,8+i*2,'#ffe3a3ff',detail);
        sprite.apply({op:'setDuration',frameId,durationMs:i<3?60:90});
    } else {
        const bob=Math.round(Math.sin(phase)*2);
        for(let radius=13;radius>=4;radius--) oval(32-radius,28+bob,2*radius,25,'#626daf40');
        oval(25,13+bob,15,17,'#e6b499ff'); oval(23,10+bob,18,10,'#392f5aff',detail);
        rect(26,29+bob,13,16,'#938bc4ff');
        line(28,43+bob,25,54,'#423d65ff',5); line(35,43+bob,39,54,'#423d65ff',5);
        line(37,30+bob,45,40+bob,'#e6b499ff',4,detail); rect(36,19+bob,2,2,'#302b46ff',detail);
    }
    sprite.apply({op:'setPivot',frameId,name:'root',x:32,y:54});
}
sprite.apply({op:'setClip',clip:{name:kind,frameIds:frames,direction:'forward',loop:kind!=='impact'}});
