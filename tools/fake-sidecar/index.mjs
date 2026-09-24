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
import * as readlineCore from 'node:readline';
import readline from 'node:readline/promises';
import { stdin as input, stdout as output } from 'node:process';

const PORT = Number(process.env.PORT ?? 7331);
const DEFAULT_TAG = 'Juan Pérez 🌹'; // el emoji debe desaparecer en el juego (la fuente no lo dibuja)

const rl = readline.createInterface({ input, output });
let currentPrompt = null;

/**
 * Imprime sin pisar lo que el usuario está escribiendo: borra la línea del prompt,
 * escribe el mensaje y vuelve a dibujar el prompt con el texto a medio escribir.
 */
function log(...args) {
  if (currentPrompt !== null) {
    readlineCore.clearLine(output, 0);
    readlineCore.cursorTo(output, 0);
  }
  console.log(...args);
  if (currentPrompt !== null) output.write(currentPrompt + rl.line);
}

async function ask(prompt) {
  currentPrompt = prompt;
  try { return (await rl.question(prompt)).trim(); }
  finally { currentPrompt = null; }
}

// ------------------------------------------------------------------ servidor

const wss = new WebSocketServer({ port: PORT });
let catalog = null;
let last = null;
let seq = 0;

wss.on('listening', () => log(`Simulador StreamTok escuchando en ws://localhost:${PORT}`));
wss.on('error', (err) => { console.error('Error del servidor:', err.message); process.exit(1); });
wss.on('connection', (ws, req) => {
  log(`+ mod conectado (${req.socket.remoteAddress})`);
  ws.on('message', (data) => handle(data.toString()));
  ws.on('close', () => log('- mod desconectado'));
});

function handle(text) {
  let msg;
  try { msg = JSON.parse(text); } catch { return log('← (no es JSON)', text); }

  if (msg.channel === 'mod-hello') {
    catalog = msg.payload;
    log(`← mod-hello: ${catalog.mod} v${catalog.version}, ${catalog.actions.length} acciones`);
    printCatalog();
  } else if (msg.channel === 'mod-ack') {
    const { id, ok, error } = msg.payload;
    log(ok ? `← ✓ ${id}` : `← ✗ ${id}: ${error}`);
  } else {
    log('←', text);
  }
}

function printCatalog() {
  if (!catalog) return log('Sin catálogo todavía: abre el juego (o pulsa Insert) para que el mod se conecte.');
  const lines = catalog.actions.map((a, i) => {
    const params = a.params.map((p) => p.name).join(', ') || 'sin parámetros';
    return `  ${String(i + 1).padStart(2)}. ${a.name} [${a.id}] (${params})${a.supportsNameTag ? ' 🏷️' : ''}`;
  });
  log(lines.join('\n'));
}

function send(payload) {
  const msg = JSON.stringify({ channel: 'mod-command', payload });
  let n = 0;
  for (const client of wss.clients) {
    if (client.readyState === 1) { client.send(msg); n++; }
  }
  log(n ? `→ ${msg}` : '→ (ningún mod conectado, no se envió)');
}

const newId = () => `c-${++seq}`;

// ------------------------------------------------------------------ parámetros

async function askParam(p) {
  if (p.type === 'enum') {
    // Se puede responder con el nombre o con el número de la opción.
    const numbered = p.options.map((o, i) => `${i + 1}=${o}`).join(' ');
    const answer = await ask(`  ${p.name} [${numbered}] (Enter = ${p.default}): `);
    if (!answer) return p.default;
    const n = Number(answer);
    if (Number.isInteger(n) && n >= 1 && n <= p.options.length) return p.options[n - 1];
    return answer; // si no es válida, el mod usa el default (y eso también es una prueba)
  }

  if (p.type === 'int') {
    const answer = await ask(`  ${p.name} [${p.min}-${p.max}] (Enter = ${p.default}): `);
    return answer ? Number(answer) : p.default;
  }

  const answer = await ask(`  ${p.name} [s/n] (Enter = ${p.default ? 's' : 'n'}): `);
  return answer ? /^(s|si|sí|y|yes|true|1)$/i.test(answer) : p.default;
}

// ------------------------------------------------------------------ bucle

log('Comandos: número = enviar acción · l = listar · r = repetir la última · x = acción inexistente · q = salir');

while (true) {
  const answer = (await ask('\nAcción: ')).toLowerCase();

  if (answer === 'q') process.exit(0);
  if (answer === 'l') { printCatalog(); continue; }
  if (answer === 'x') { send({ id: newId(), action: 'no_existe', params: {} }); continue; }
  if (answer === 'r') {
    if (last) send({ id: newId(), ...last });
    else log('Todavía no enviaste nada.');
    continue;
  }
  if (!answer) continue;

  if (!catalog) { printCatalog(); continue; }
  const action = catalog.actions[Number(answer) - 1];
  if (!action) { log('Número inválido. Usa "l" para ver la lista.'); continue; }

  const params = {};
  for (const p of action.params) params[p.name] = await askParam(p);

  let nameTag;
  if (action.supportsNameTag) {
    nameTag = (await ask(`  nameTag (Enter = ${DEFAULT_TAG}): `)) || DEFAULT_TAG;
  }

  last = { action: action.id, params, nameTag, notify: `${nameTag ?? 'Alguien'} activó: ${action.name}` };
  send({ id: newId(), ...last });
}
