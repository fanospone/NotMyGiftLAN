const $ = id => document.getElementById(id);

const tokenKey = 'nmg-token';
const roomKey = 'nmg-room';
const nameKey = 'nmg-name';

function generateToken() {
    if (window.crypto?.randomUUID) return window.crypto.randomUUID();

    const bytes = new Uint8Array(16);
    window.crypto.getRandomValues(bytes);
    return Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('');
}

let token = localStorage.getItem(tokenKey);
if (!token) {
    token = generateToken();
    localStorage.setItem(tokenKey, token);
}

let room = localStorage.getItem(roomKey) || '';
let state = null;

$('name').value = localStorage.getItem(nameKey) || '';

const conn = new signalR.HubConnectionBuilder()
    .withUrl('/gamehub')
    .withAutomaticReconnect()
    .build();

function toast(message) {
    $('toast').textContent = message;
    $('toast').hidden = false;
    setTimeout(() => $('toast').hidden = true, 3800);
}

async function invoke(method, ...args) {
    try {
        return await conn.invoke(method, ...args);
    } catch (e) {
        toast(e.message.replace(/^.*HubException: /, ''));
        return null;
    }
}

function el(tag, text, cls) {
    const x = document.createElement(tag);
    if (text !== undefined) x.textContent = text;
    if (cls) x.className = cls;
    return x;
}

function btn(text, callback, cls = '') {
    const b = el('button', text, cls);
    b.onclick = callback;
    return b;
}

function clear(id) {
    $(id).replaceChildren();
}

function directionArrow(direction) {
    switch (direction) {
        case 'Left': return '←';
        case 'Right': return '→';
        case 'Any': return '↔';
        default: return '?';
    }
}

function directionLabel(direction) {
    switch (direction) {
        case 'Left': return 'Kiri';
        case 'Right': return 'Kanan';
        case 'Any': return 'Bebas';
        default: return 'Tidak diketahui';
    }
}

async function enter(create) {
    const name = $('name').value.trim();
    const code = $('joinCode').value.trim().toUpperCase();

    if (name.length < 2) {
        toast('Nama minimal 2 karakter.');
        return;
    }

    localStorage.setItem(nameKey, name);

    const result = create
        ? await invoke('CreateRoom', name, token)
        : await invoke('JoinRoom', code, name, token);

    if (result) {
        room = result.code;
        localStorage.setItem(roomKey, room);
        $('home').hidden = true;
        $('game').hidden = false;
    }
}

$('create').onclick = () => enter(true);
$('join').onclick = () => enter(false);

$('copy').onclick = async () => {
    try {
        await navigator.clipboard.writeText(room);
        toast('Kode room disalin.');
    } catch {
        toast('Kode: ' + room);
    }
};

$('closeRoom').onclick = async () => {
    if (!room) return;

    const confirmed = confirm(
        'Apakah Anda yakin ingin menutup room ini?\n\n' +
        'Seluruh pemain akan dikeluarkan dan room tidak dapat digunakan kembali.'
    );

    if (!confirmed) return;

    await invoke('CloseRoom', room, token);
};

conn.on('State', s => {
    state = s;
    render();
});

conn.on('RoomClosed', () => {
    state = null;
    room = '';

    localStorage.removeItem(roomKey);

    $('game').hidden = true;
    $('home').hidden = false;

    $('status').textContent = 'Room ditutup';

    clear('players');
    clear('logs');
    clear('cards');
    clear('controls');

    $('roomCode').textContent = '';

    toast('Host telah menutup room. Semua pemain telah dikeluarkan.');
});

conn.onreconnecting(() => {
    $('status').textContent = 'Menghubungkan ulang...';
});

conn.onreconnected(async () => {
    $('status').textContent = 'Terhubung';

    if (room) {
        await invoke(
            'JoinRoom',
            room,
            localStorage.getItem(nameKey) || 'Pemain',
            token
        );
    }
});

conn.onclose(() => {
    $('status').textContent = 'Terputus';
});

function renderPlayers(s) {
    clear('players');

    for (const p of s.players) {
        const item = el(
            'div',
            undefined,
            'player' + (p.id === s.currentId && s.phase === 'play' ? ' turn' : '')
        );

        const info = el('div');

        info.append(
            el('strong', p.name + (p.id === s.viewerId ? ' (Anda)' : '')),
            el(
                'div',
                (p.connected ? '🟢 Online' : '🔴 Offline') +
                (p.id === s.hostId ? ' · Host' : ''),
                'meta'
            )
        );

        item.append(
            info,
            el('strong', '⚠️ ' + p.penalties)
        );

        $('players').append(item);
    }
}

function renderLogs(s) {
    clear('logs');

    for (const message of s.log) {
        $('logs').append(el('div', message, 'log'));
    }
}

function renderGiftShop(s) {
    const panel = $('giftShopPanel');
    const container = $('giftShop');

    container.replaceChildren();

    panel.hidden = !['memorize', 'play'].includes(s.phase);

    if (panel.hidden) return;

    $('shopCount').textContent =
        `Sisa hadiah di Gift Shop: ${s.deckCount} kartu`;

    for (const card of s.giftShop) {
        const item = el('div', undefined, 'shop-card');

        item.append(
            el('div', card.emoji, 'emoji'),
            el('div', card.name, 'small'),
            el('div', directionArrow(card.direction), 'direction')
        );

        container.append(item);
    }
}

function renderMemorize(s, host) {
    $('headline').textContent = '🧠 Hafalkan semua hadiah!';
    $('description').textContent =
        'Ingat barang yang dipegang setiap pemain. Setelah kartu ditutup, semua hadiah menjadi rahasia.';

    for (const p of s.players) {
        const card = el('div', undefined, 'gift');

        card.append(
            el('div', p.card?.emoji || '❓', 'emoji'),
            el('div', p.card?.name || 'Kosong', 'small'),
            el('div', p.name, 'small'),
            el('div', directionArrow(p.card?.direction), 'direction')
        );

        $('cards').append(card);
    }

    if (host) {
        $('controls').append(
            btn('🔒 Tutup Kartu & Mulai', () =>
                invoke('HideCards', room, token)
            )
        );
    } else {
        $('controls').append(
            el('p', 'Menunggu host menutup kartu...')
        );
    }
}

function renderDrawnCard(s) {
    if (!s.drawnCard) return;

    const card = el('div', undefined, 'gift drawn-card');

    card.append(
        el('div', s.drawnCard.emoji || '🎁', 'emoji'),
        el('div', s.drawnCard.name || 'Hadiah', 'small'),
        el('div', directionArrow(s.drawnCard.direction), 'direction'),
        el('div', directionLabel(s.drawnCard.direction), 'small')
    );

    $('cards').append(card);
}

function renderPending(s) {
    const pending = s.pending;

    const sender = s.players.find(p => p.id === pending.fromId);
    const receiver = s.players.find(p => p.id === pending.toId);

    $('description').textContent =
        `${sender?.name || 'Pemain'} mengaku memberikan hadiah "${pending.claim}" kepada ${receiver?.name || 'pemain'}.`;

    const card = el('div', undefined, 'gift pending-card');

    card.append(
        el('div', '🎁', 'emoji'),
        el('div', 'HADIAH RAHASIA', 'small'),
        el('div', directionArrow(pending.direction), 'direction'),
        el('div', directionLabel(pending.direction), 'small')
    );

    $('cards').append(card);

    if (pending.toId === s.viewerId) {
        $('controls').append(
            btn('✅ Terima', () =>
                invoke('Respond', room, token, false)
            ),
            btn('🚨 Tantang!', () =>
                invoke('Respond', room, token, true),
                'secondary'
            )
        );
    } else {
        $('controls').append(
            el('p', `Menunggu ${receiver?.name || 'penerima'} mengambil keputusan...`)
        );
    }
}

function renderGiveGift(s) {
    $('description').textContent =
        'Lihat hadiah yang Anda pegang, lalu berikan kepada pemain berikutnya. Anda boleh berkata jujur atau bluffing!';

    renderDrawnCard(s);

    const claimSelect = el('select');

    for (const choice of s.choices) {
        const option = el('option', choice);
        option.value = choice;
        claimSelect.append(option);
    }

    $('controls').append(
        el('label', 'Klaim nama hadiah'),
        claimSelect
    );

    let targetSelect = null;

    if (s.drawnCard?.direction === 'Any') {
        $('controls').append(
            el('p', '↔ Kartu ini dapat diberikan kepada pemain mana pun.')
        );

        targetSelect = el('select');

        targetSelect.append(
            el('option', 'Pilih penerima')
        );

        for (const player of s.players) {
            if (player.id === s.viewerId) continue;

            const option = el('option', player.name);
            option.value = player.id;
            targetSelect.append(option);
        }

        $('controls').append(targetSelect);
    } else {
        $('controls').append(
            el(
                'p',
                `Arah kartu: ${directionArrow(s.drawnCard?.direction)} ${directionLabel(s.drawnCard?.direction)}`
            )
        );
    }

    $('controls').append(
        btn('🎁 Berikan Hadiah', () => {
            const targetId = targetSelect?.value || null;

            if (targetSelect && !targetId) {
                toast('Pilih pemain penerima terlebih dahulu.');
                return;
            }

            invoke(
                'GiveGift',
                room,
                token,
                claimSelect.value,
                targetId
            );
        })
    );
}

function renderPlay(s) {
    const turn = s.players.find(p => p.id === s.currentId);

    $('headline').textContent =
        '🎁 Giliran ' + (turn?.name || '');

    if (s.pending) {
        renderPending(s);
        return;
    }

    if (s.currentId === s.viewerId) {
        renderGiveGift(s);
    } else {
        $('description').textContent =
            `Menunggu giliran ${turn?.name || 'pemain lain'}...`;

        renderDrawnCard(s);
    }
}

function renderFinished(s) {
    $('headline').textContent = '🏆 Permainan Selesai';

    const minPenalty = Math.min(
        ...s.players.map(p => p.penalties)
    );

    const winners = s.players.filter(
        p => p.penalties === minPenalty
    );

    $('description').textContent =
        'Pemenang: ' +
        winners.map(p => p.name).join(', ') +
        ` dengan ${minPenalty} penalti.`;

    $('controls').append(
        el('p', 'Terima kasih sudah bermain NOT MY GIFT!')
    );

    if (s.hostId === s.viewerId) {
        $('controls').append(
            btn('🔄 Main Lagi', () =>
                invoke('BackToLobby', room, token)
            )
        );
    }
}

function render() {
    const s = state;
    if (!s) return;

    $('home').hidden = true;
    $('game').hidden = false;

    $('status').textContent = '🟢 Terhubung';
    $('roomCode').textContent = s.code;
    $('phase').textContent = s.phase.toUpperCase();

    clear('cards');
    clear('controls');

    renderPlayers(s);
    renderLogs(s);
    renderGiftShop(s);

    const host = s.hostId === s.viewerId;

    $('closeRoom').hidden = !host;

    if (s.phase === 'lobby') {
        $('headline').textContent = 'Menunggu pemain...';
        $('description').textContent =
            `${s.players.length}/8 pemain. Minimal 3 pemain untuk mulai.`;

        if (host) {
            $('controls').append(
                btn('▶️ Mulai Permainan', () =>
                    invoke('StartGame', room, token)
                )
            );
        }
    }

    if (s.phase === 'memorize') {
        renderMemorize(s, host);
    }

    if (s.phase === 'play') {
        renderPlay(s);
    }

    if (s.phase === 'finished') {
        renderFinished(s);
    }
}

(async () => {
    try {
        await conn.start();

        $('status').textContent = '🟢 Terhubung';

        if (room) {
            const result = await invoke(
                'JoinRoom',
                room,
                localStorage.getItem(nameKey) || 'Pemain',
                token
            );

            if (!result) {
                room = '';
                localStorage.removeItem(roomKey);
            }
        }
    } catch (e) {
        $('status').textContent = '❌ Server tidak tersedia';
        toast('Gagal terhubung. Periksa server SignalR.');
    }
})();