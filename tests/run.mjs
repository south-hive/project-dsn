import { spawn, execFileSync } from 'node:child_process';
import { mkdirSync, writeFileSync, readFileSync, createWriteStream } from 'node:fs';
import { finished } from 'node:stream/promises';
import { fileURLToPath } from 'node:url';
import { resolve, join } from 'node:path';
import { platform, arch, release } from 'node:os';
import { createHash } from 'node:crypto';

const root = fileURLToPath(new URL('../', import.meta.url));
const cwd = join(root, 'prototype');
const runId = new Date().toISOString().replaceAll(':', '-') + '-' + process.pid;
const directory = join(root, 'tests/results', runId);
mkdirSync(directory, { recursive: true });
const git = (...args) => execFileSync('git', args, { cwd: root, encoding: 'utf8' }).trim();
const files = git('ls-files', '--cached', '--others', '--exclude-standard').split('\n').filter(Boolean);
const hashes = Object.fromEntries(files.filter(p => /^(prototype\/|tests\/)|^\.gitignore$/.test(p)).map(p =>
  [p, createHash('sha256').update(readFileSync(join(root, p))).digest('hex')]));
const report = { runId, started: new Date().toISOString(), commit: git('rev-parse', 'HEAD'),
  branch: git('branch', '--show-current'), status: git('status', '--short'),
  environment: { node: process.version, npm: execFileSync('npm', ['--version'], { encoding: 'utf8' }).trim(),
    platform: platform(), arch: arch(), release: release() }, hashes, steps: [], result: 'running' };
const save = () => writeFileSync(join(directory, 'summary.json'), JSON.stringify(report, null, 2) + '\n');
save();

async function run(name, command, args, expectedTests) {
  const outPath = join(directory, name + '.stdout.log'); const errPath = join(directory, name + '.stderr.log');
  const stdout = createWriteStream(outPath); const stderr = createWriteStream(errPath);
  const child = spawn(command, args, { cwd, detached: process.platform !== 'win32', stdio: ['ignore', 'pipe', 'pipe'] });
  const started = Date.now(); let timedOut = false; let spawnError;
  const kill = signal => {
    if (!child.pid) return;
    try { if (process.platform === 'win32') child.kill(signal); else process.kill(-child.pid, signal); }
    catch (error) { if (error.code !== 'ESRCH') throw error; }
  };
  child.stdout.pipe(stdout); child.stderr.pipe(stderr);
  child.on('error', error => { spawnError = error.message; });
  const timer = setTimeout(() => { timedOut = true; kill('SIGKILL'); }, 60_000);
  const [exitCode, signal] = await new Promise(resolve => child.once('close', (...result) => resolve(result)));
  clearTimeout(timer); kill('SIGKILL'); // Reap any test descendants left after the parent exits.
  await Promise.all([finished(stdout), finished(stderr)]);
  const tap = readFileSync(outPath, 'utf8'); const counts = {};
  for (const key of ['tests', 'pass', 'fail', 'cancelled', 'skipped', 'todo']) {
    const match = tap.match(new RegExp('^# ' + key + ' (\\d+)$', 'm'));
    if (match) counts[key] = Number(match[1]);
  }
  const testsOkay = expectedTests === undefined || (counts.tests === expectedTests && counts.pass === expectedTests
    && ['fail', 'cancelled', 'skipped', 'todo'].every(key => counts[key] === 0));
  const step = { name, command: [command, ...args], cwd, exitCode, signal, timedOut, spawnError,
    durationMs: Date.now() - started, counts, status: exitCode === 0 && !timedOut && !spawnError && testsOkay ? 'passed' : 'failed',
    stdout: name + '.stdout.log', stderr: name + '.stderr.log' };
  report.steps.push(step); save(); console.log(`${name}: ${step.status}`); return step;
}

try {
  const build = await run('build', 'npm', ['run', 'build']);
  if (build.status === 'passed') {
    await run('verification', 'npm', ['run', 'verify'], 27);
    for (let round = 1; round <= 3; round++) await run(`validation-${round}`, 'npm', ['run', 'validate'], 19);
    await run('demo', 'npm', ['run', 'demo']);
  } else {
    for (const name of ['verification', 'validation-1', 'validation-2', 'validation-3', 'demo']) {
      report.steps.push({ name, status: 'not-run', reason: 'build failed' });
    }
  }
  report.result = report.steps.every(step => step.status === 'passed') ? 'passed' : 'failed';
} catch (error) { report.result = 'failed'; report.error = String(error.stack ?? error); }
report.finished = new Date().toISOString(); save();
console.log(`Result: ${report.result}; artifacts: ${resolve(directory)}`);
process.exitCode = report.result === 'passed' ? 0 : 1;
