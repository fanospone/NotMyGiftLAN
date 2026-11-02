
const $ = id => document.getElementById(id);

const tokenKey = 'nmg-token',
    roomKey = 'nmg-room',
    nameKey = 'nmg-name';

// Generate unique player token
function generateToken() {
    // Browser secure context
    if (window.crypto && typeof window.crypto.randomUUID === 'function') {
        return window.crypto.randomUUID();
    }

    // Fallback menggunakan Web Crypto
    if (window.crypto && typeof window.crypto.getRandomValues === 'function') {
        const bytes = new Uint8Array(16);
        window.crypto.getRandomValues(bytes);

        return Array.from(bytes, b =>
            b.toString(16).padStart(2, '0')
        ).join('');
    }

    // Fallback browser lama
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c => {
        const r = Math.random() * 16 | 0;
        const v = c === 'x' ? r : (r & 0x3 | 0x8);
        return v.toString(16);
    });
}

let token = localStorage.getItem(tokenKey);

if (!token) {
    token = generateToken();
    localStorage.setItem(tokenKey, token);
}

let room = localStorage.getItem(roomKey) || '',
    state = null;

$('name').value = localStorage.getItem(nameKey) || '';

const conn=new signalR.HubConnectionBuilder().withUrl('/gamehub').withAutomaticReconnect().build();
function toast(message){$('toast').textContent=message;$('toast').hidden=false;setTimeout(()=>$('toast').hidden=true,3800)}
async function invoke(method,...args){try{return await conn.invoke(method,...args)}catch(e){toast(e.message.replace(/^.*HubException: /,''));return null}}
function el(tag,text,cls){const x=document.createElement(tag);if(text!==undefined)x.textContent=text;if(cls)x.className=cls;return x}
function btn(text,callback,cls=''){const b=el('button',text,cls);b.onclick=callback;return b}
function clear(id){$(id).replaceChildren()}
async function enter(create){const name=$('name').value.trim(),code=$('joinCode').value.trim().toUpperCase();if(name.length<2){toast('Nama minimal 2 karakter.');return}localStorage.setItem(nameKey,name);const result=create?await invoke('CreateRoom',name,token):await invoke('JoinRoom',code,name,token);if(result){room=result.code;localStorage.setItem(roomKey,room);$('home').hidden=true;$('game').hidden=false}}
$('create').onclick=()=>enter(true);$('join').onclick=()=>enter(false);
$('copy').onclick=async()=>{try{await navigator.clipboard.writeText(room);toast('Kode room disalin.')}catch{toast('Kode: '+room)}};
conn.on('State',s=>{state=s;render()});
conn.onreconnecting(()=>{$('status').textContent='Menghubungkan ulang...'});
conn.onreconnected(async()=>{ $('status').textContent='Terhubung';if(room)await invoke('JoinRoom',room,localStorage.getItem(nameKey)||'Pemain',token)});
conn.onclose(()=>{$('status').textContent='Terputus'});
function render(){const s=state; if(!s)return;$('home').hidden=true;$('game').hidden=false;$('roomCode').textContent=s.code;$('phase').textContent=s.phase.toUpperCase();clear('players');clear('logs');clear('cards');clear('controls');
for(const p of s.players){const item=el('div',undefined,'player'+(p.id===s.currentId&&s.phase==='play'?' turn':''));const info=el('div');info.append(el('strong',p.name+(p.id===s.viewerId?' (Anda)':'')),el('div',(p.connected?'🟢 Online':'🔴 Offline')+(p.id===s.hostId?' · Host':''),'meta'));item.append(info,el('strong','⚠️ '+p.penalties));$('players').append(item)}
for(const msg of s.log)$('logs').append(el('div',msg,'log'));
const me=s.players.find(p=>p.id===s.viewerId),host=s.hostId===s.viewerId;
if(s.phase==='lobby'){$('headline').textContent='Menunggu pemain...';$('description').textContent=`${s.players.length}/8 pemain. Minimal 3 untuk mulai.`;if(host)$('controls').append(btn('▶️ Mulai Permainan',()=>invoke('StartGame',room,token)))}
if(s.phase==='memorize'){$('headline').textContent='🧠 Hafalkan semua hadiah!';$('description').textContent='Ingat posisi dan barangnya. Setelah ditutup, semua kartu menjadi rahasia.';for(const p of s.players){const c=el('div',undefined,'gift');c.append(el('div',p.card?.emoji||'❓','emoji'),el('div',p.card?.name||'Kosong','small'),el('div',p.name,'small'));$('cards').append(c)}if(host)$('controls').append(btn('🔒 Tutup Kartu & Mulai',()=>invoke('HideCards',room,token)))}
if(s.phase==='play'){
 const turn=s.players.find(p=>p.id===s.currentId);$('headline').textContent='🎁 Giliran '+(turn?.name||'');
 const back=el('div',undefined,'gift');back.append(el('div','🎁','emoji'),el('div','KARTU RAHASIA','small'));$('cards').append(back);
 if(s.pending){const sender=s.players.find(p=>p.id===s.pending.fromId),receiver=s.players.find(p=>p.id===s.pending.toId);$('description').textContent=`${sender?.name} mengaku memberikan ${s.pending.claim} kepada ${receiver?.name}.`;
 if(s.pending.toId===s.viewerId){$('controls').append(btn('✅ Terima',()=>invoke('Respond',room,token,false)),btn('🚨 Tantang!',()=>invoke('Respond',room,token,true),'secondary'))}
 else $('controls').append(el('p','Menunggu keputusan penerima...'));
 }else if(s.currentId===s.viewerId){$('description').textContent='Pilih barang yang Anda klaim sebagai hadiah. Boleh berkata benar atau bluffing!';const select=el('select');for(const choice of s.choices){const opt=el('option',choice);opt.value=choice;select.append(opt)}$('controls').append(select,btn('🎁 Berikan Hadiah',()=>invoke('GiveGift',room,token,select.value)))}else $('description').textContent='Tunggu giliran pemain lain...';
 }
if(s.phase==='finished'){$('headline').textContent='🏆 Permainan Selesai';const min=Math.min(...s.players.map(p=>p.penalties));$('description').textContent='Pemenang: '+s.players.filter(p=>p.penalties===min).map(p=>p.name).join(', ')+' (penalti terendah).';if(host)$('controls').append(btn('🔄 Main Lagi',()=>invoke('BackToLobby',room,token)))}
}
(async()=>{try{await conn.start();$('status').textContent='🟢 Terhubung';if(room){const r=await invoke('JoinRoom',room,localStorage.getItem(nameKey)||'Pemain',token);if(!r){room='';localStorage.removeItem(roomKey)}}}catch(e){$('status').textContent='❌ Server tidak tersedia';toast('Gagal terhubung. Periksa server SignalR.')}})();
