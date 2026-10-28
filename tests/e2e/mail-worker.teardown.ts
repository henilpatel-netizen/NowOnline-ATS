import fs from 'node:fs';
import { PID_FILE, stopWorker } from './mail-worker.setup';

/** Stops the Ats.Worker that mail-worker.setup.ts started. */
export default async function globalTeardown(): Promise<void> {
  if (!fs.existsSync(PID_FILE)) return;
  const pid = Number(fs.readFileSync(PID_FILE, 'utf8'));
  fs.rmSync(PID_FILE, { force: true });
  stopWorker(pid);
}
