// Minimal MCP client for the test server, for terminals and scripts that have no MCP client of their own.
// It starts the server over stdio, does the initialize handshake, calls one tool and, with --wait,
// keeps calling wait_job until the job is done. The server (and its job) ends when this exits.
//
//   node Tools/TestMcp/mcp-call.mjs --list
//   node Tools/TestMcp/mcp-call.mjs run_unit_tests --wait
//   node Tools/TestMcp/mcp-call.mjs run_match "{\"rounds\":2,\"dropOneAfter\":5}" --wait
//
// Exit code: 0 = tool ok (and with --wait, job passed), 1 = failed, 2 = usage / server problem.
import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import fs from 'node:fs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const dll = path.join(repo, 'artifacts', 'bin', 'TestMcp', 'debug', 'OnlineSeverGame.TestMcp.dll');

const flags = process.argv.slice(2).filter(a => a.startsWith('--'));
const positional = process.argv.slice(2).filter(a => !a.startsWith('--'));
const [tool, rawArgs] = positional;
const wait = flags.includes('--wait');

if (!fs.existsSync(dll)) {
  console.error(`not built yet: ${dll}\nrun: dotnet build Tools/TestMcp`);
  process.exit(2);
}
let toolArgs = {};
if (rawArgs) {
  try { toolArgs = JSON.parse(rawArgs); } catch { console.error(`arguments must be JSON, got: ${rawArgs}`); process.exit(2); }
}

const child = spawn('dotnet', [dll], { cwd: repo, stdio: ['pipe', 'pipe', 'inherit'] });
const waiting = new Map();
let nextId = 1;
createInterface({ input: child.stdout }).on('line', line => {
  const msg = JSON.parse(line);
  const resolve = waiting.get(msg.id);
  if (resolve) { waiting.delete(msg.id); resolve(msg); }
});
child.on('exit', code => {
  if (waiting.size > 0) { console.error(`server exited (${code}) with ${waiting.size} request(s) unanswered`); process.exit(2); }
});

function request(method, params) {
  const id = nextId++;
  child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
  return new Promise(resolve => waiting.set(id, resolve));
}

async function call(name, args) {
  const r = await request('tools/call', { name, arguments: args });
  if (r.error) throw new Error(`${name}: ${r.error.message}`);
  const text = r.result.content.map(c => c.text).join('\n');
  let json = null;
  try { json = JSON.parse(text); } catch { /* plain-text error message */ }
  return { text, json, isError: r.result.isError };
}

let exitCode = 0;
try {
  const init = await request('initialize', {
    protocolVersion: '2025-06-18',
    capabilities: {},
    clientInfo: { name: 'mcp-call', version: '1.0.0' },
  });
  if (init.error) throw new Error(init.error.message);
  child.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');

  if (!tool || flags.includes('--list')) {
    const list = await request('tools/list', {});
    const { name, version } = init.result.serverInfo;
    console.log(`${name} ${version} (protocol ${init.result.protocolVersion})`);
    for (const t of list.result.tools) console.log(`- ${t.name}: ${t.description}`);
  } else {
    const first = await call(tool, toolArgs);
    console.log(first.text);
    if (first.isError) exitCode = 1;
    else if (wait && first.json?.jobId) {
      const jobId = first.json.jobId;
      let status;
      for (;;) {
        status = (await call('wait_job', { jobId, timeoutSeconds: 55 })).json;
        if (!['queued', 'running'].includes(status.state)) break;
        console.error(`[${jobId}] ${status.state}: ${status.step} (${status.elapsedSeconds ?? 0} s)`);
      }
      console.log(JSON.stringify(status, null, 2));
      exitCode = status.state === 'passed' ? 0 : 1;
    }
  }
} catch (e) {
  console.error(e.message);
  exitCode = 2;
}
child.stdin.end();
child.on('exit', () => process.exit(exitCode));
