import { execSync, spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';

/** Every mail the app sends during the run lands here as an .eml file (tests/e2e/mailbox.ts reads it). */
export const MAIL_DIR = path.resolve(__dirname, '../../artifacts/mail');
export const PID_FILE = path.resolve(__dirname, '../../artifacts/mail-worker.pid');
const LOG_FILE = path.resolve(__dirname, '../../artifacts/mail-worker.log');

/** Kills the worker with the process tree under it (dotnet run starts the app as a child). */
export function stopWorker(pid: number): void {
  try {
    if (process.platform === 'win32') execSync(`taskkill /PID ${pid} /T /F`, { stdio: 'ignore' });
    else process.kill(-pid, 'SIGKILL');
  } catch {
    // Already gone.
  }
}

/**
 * Starts Ats.Worker writing mail to MAIL_DIR instead of SMTP. Every Email setting that could send real
 * mail is overridden here, including a developer's user-secrets (RedirectAllTo, Smtp:Host).
 */
export default async function globalSetup(): Promise<void> {
  fs.rmSync(MAIL_DIR, { recursive: true, force: true });
  fs.mkdirSync(MAIL_DIR, { recursive: true });

  const log = fs.createWriteStream(LOG_FILE);
  const child = spawn('dotnet', ['run', '--project', 'src/Ats.Worker'], {
    cwd: path.resolve(__dirname, '../..'),
    env: {
      ...process.env,
      DOTNET_ENVIRONMENT: 'Development',
      Email__PickupDirectory: MAIL_DIR,
      Email__PollSeconds: '1',
      Email__Smtp__Host: '',
      Email__RedirectAllTo: '',
      Email__FromAddress: 'e2e@ats.test',
    },
    stdio: ['ignore', 'pipe', 'pipe'],
    detached: process.platform !== 'win32',
  });
  if (!child.pid) throw new Error('Could not start Ats.Worker.');
  fs.writeFileSync(PID_FILE, String(child.pid));

  // Keep draining both pipes for the whole run, or the worker blocks once the pipe buffer is full.
  let output = '';
  let started = false;
  await new Promise<void>((resolve, reject) => {
    const timer = setTimeout(() => {
      stopWorker(child.pid!);
      reject(new Error(`Ats.Worker did not start within 180 s. See ${LOG_FILE}.`));
    }, 180_000);
    const onData = (chunk: Buffer) => {
      log.write(chunk);
      if (started) return;
      output += chunk.toString();
      if (output.includes('Application started')) {
        started = true;
        clearTimeout(timer);
        resolve();
      }
    };
    child.stdout!.on('data', onData);
    child.stderr!.on('data', onData);
    child.on('exit', code => {
      clearTimeout(timer);
      reject(new Error(`Ats.Worker exited with code ${code} before it started. See ${LOG_FILE}.`));
    });
  });
}
