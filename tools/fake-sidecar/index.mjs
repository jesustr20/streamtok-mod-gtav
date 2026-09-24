// Sidecar falso para probar el mod sin levantar StreamTok completo.
// Envía eventos con el mismo sobre que el sidecar real: { channel: "live-event", payload }.
//
//   cd tools/fake-sidecar
//   npm install
//   npm start
//
// Teclas: [g] regalo  [l] like  [f] follow  [s] share  [c] chat  [a] auto on/off  [q] salir
// ¡No lo corras a la vez que el sidecar real: ambos usan el puerto 7331!

import { WebSocketServer } from 'ws';
import readline from 'node:readline';

const PORT = Number(process.env.PORT ?? 7331);
const wss = new WebSocketServer({ port: PORT });

const users = ['juan_perez', 'maria.lima', 'gamer_pe', 'la~trampa~', 'xX_Sniper_Xx'];
const gifts = [
  { giftName: 'Rosa', value: 1 },
  { giftName: 'TikTok', value: 1 },
  { giftName: 'Corazón', value: 5 },
  { giftName: 'León', value: 29999 },
];
const pick = (arr) => arr[Math.floor(Math.random() * arr.length)];

function makeEvent(kind) {
  const base = { event: kind, username: pick(users), timestamp: Date.now() };
  if (kind === 'gift') return { ...base, ...pick(gifts) };
  if (kind === 'chat') return { ...base, text: pick(['hola!', 'spawnea un tanque', 'GG', '~r~hack']) };
  return base;
}

function broadcast(kind) {
  const msg = JSON.stringify({ channel: 'live-event', payload: makeEvent(kind) });
  let n = 0;
  for (const client of wss.clients) {
    if (client.readyState === 1) { client.send(msg); n++; }
  }
  console.log(`→ ${msg}  (${n} cliente${n === 1 ? '' : 's'})`);
}

wss.on('listening', () => console.log(`Fake sidecar escuchando en ws://localhost:${PORT}`));
wss.on('connection', (ws, req) => {
  console.log(`+ cliente conectado (${req.socket.remoteAddress}). Total: ${wss.clients.size}`);
  ws.on('close', () => console.log(`- cliente desconectado. Total: ${wss.clients.size}`));
});
wss.on('error', (err) => { console.error('Error del servidor:', err.message); process.exit(1); });

let auto = null;
const kinds = { g: 'gift', l: 'like', f: 'follow', s: 'share', c: 'chat' };

readline.emitKeypressEvents(process.stdin);
if (process.stdin.isTTY) process.stdin.setRawMode(true);
process.stdin.on('keypress', (_str, key) => {
  if (!key) return;
  if (key.name === 'q' || (key.ctrl && key.name === 'c')) process.exit(0);
  if (key.name === 'a') {
    if (auto) { clearInterval(auto); auto = null; console.log('auto: OFF'); }
    else { auto = setInterval(() => broadcast(pick(Object.values(kinds))), 3000); console.log('auto: ON (cada 3s)'); }
    return;
  }
  if (kinds[key.name]) broadcast(kinds[key.name]);
});
console.log('Teclas: [g] regalo [l] like [f] follow [s] share [c] chat [a] auto [q] salir');
