// Simulador de StreamTok para probar el mod sin la app.
// Habla el mismo contrato que StreamTok:
//   ← mod-hello   el mod publica su catálogo al conectarse
//   → mod-command enviamos una acción con parámetros, nameTag y notify
//   ← mod-ack     el mod responde ok / error
//
//   cd tools/fake-sidecar
//   npm install
//   npm start
//
// ¡No lo corras a la vez que el sidecar real: ambos usan el puerto 7331!

import { WebSocketServer } from 'ws';
import readline from 'node:readline/promises';
import { stdin as input, stdout as output } from 'node:process';

const PORT = Number(process.env.PORT ?? 7331);
const DEFAULT_TAG = 'Juan Pérez 🌹'; // el emoji debe desaparecer en el juego (la fuente no lo dibuja)

const wss = new WebSocketServer({ port: PORT });
let catalog = null;
let last = null;
let seq = 0;

wss.on('listening', () => console.log(`Simulador StreamTok escuchando en ws://localhost:${PORT}`));
wss.on('error', (err) => { console.error('Error del servidor:', err.message); process.exit(1); });
wss.on('connection', (ws, req) => {
  console.log(`\n+ mod conectado (${req.socket.remoteAddress})`);
  ws.on('message', (data) => handle(data.toString()));
  ws.on('close', () => console.log('\n- mod desconectado'));
});

function handle(text) {
  let msg;
  try { msg = JSON.parse(text); } catch { return console.log('\n← (no es JSON)', text); }

  if (msg.channel === 'mod-hello') {
    catalog = msg.payload;
    console.log(`\n← mod-hello: ${catalog.mod} v${catalog.version}, ${catalog.actions.length} acciones`);
    printCatalog();
  } else if (msg.channel === 'mod-ack') {
    const { id, ok, error } = msg.payload;
    console.log(ok ? `\n← ✓ ${id}` : `\n← ✗ ${id}: ${error}`);
  } else {
    console.log('\n←', text);
  }
}

function printCatalog() {
  if (!catalog) return console.log('Sin catálogo todavía: abre el juego (o pulsa Insert) para que el mod se conecte.');
  catalog.actions.forEach((a, i) => {
    const params = a.params.map((p) => p.name).join(', ') || 'sin parámetros';
    console.log(`  ${String(i + 1).padStart(2)}. ${a.name} [${a.id}] (${params})${a.supportsNameTag ? ' 🏷️' : ''}`);
  });
}

function send(payload) {
  const msg = JSON.stringify({ channel: 'mod-command', payload });
  let n = 0;
  for (const client of wss.clients) {
    if (client.readyState === 1) { client.send(msg); n++; }
  }
  console.log(n ? `→ ${msg}` : '→ (ningún mod conectado, no se envió)');
}

const newId = () => `c-${++seq}`;

async function askParam(rl, p) {
  let hint;
  if (p.type === 'int') hint = `${p.min}-${p.max}`;
  else if (p.type === 'enum') hint = p.options.join('|');
  else hint = 's/n';

  const answer = (await rl.question(`  ${p.name} [${hint}] (Enter = ${p.default}): `)).trim();
  if (!answer) return p.default;
  if (p.type === 'int') return Number(answer);
  if (p.type === 'bool') return /^(s|si|sí|y|yes|true|1)$/i.test(answer);
  return answer; // enum: si no es válida, el mod usa el default (y eso también es una prueba)
}

const rl = readline.createInterface({ input, output });
console.log('Comandos: número = enviar acción · l = listar · r = repetir la última · x = acción inexistente · q = salir');

while (true) {
  const answer = (await rl.question('\nAcción: ')).trim().toLowerCase();

  if (answer === 'q') process.exit(0);
  if (answer === 'l') { printCatalog(); continue; }
  if (answer === 'x') { send({ id: newId(), action: 'no_existe', params: {} }); continue; }
  if (answer === 'r') {
    if (last) send({ id: newId(), ...last });
    else console.log('Todavía no enviaste nada.');
    continue;
  }

  if (!catalog) { printCatalog(); continue; }
  const action = catalog.actions[Number(answer) - 1];
  if (!action) { console.log('Número inválido. Usa "l" para ver la lista.'); continue; }

  const params = {};
  for (const p of action.params) params[p.name] = await askParam(rl, p);

  let nameTag;
  if (action.supportsNameTag) {
    nameTag = (await rl.question(`  nameTag (Enter = ${DEFAULT_TAG}): `)).trim() || DEFAULT_TAG;
  }

  last = { action: action.id, params, nameTag, notify: `${nameTag ?? 'Alguien'} activó: ${action.name}` };
  send({ id: newId(), ...last });
}
