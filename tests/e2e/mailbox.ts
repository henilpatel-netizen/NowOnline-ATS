import fs from 'node:fs';
import path from 'node:path';
import { expect } from '@playwright/test';
import { MAIL_DIR } from './mail-worker.setup';

export interface Mail {
  subject: string;
  text: string;
  /** The first https:// URL in the text part, or null. */
  link: string | null;
}

// A mail is returned at most once, and mail written before this module loaded (an earlier worker
// process of the run) is never matched, so a later wait cannot pick up an earlier test's mail.
const consumed = new Set<string>();
const loadedAt = Date.now();

/** Waits for an unread mail to `to` whose subject contains `subjectContains`, and marks it read. */
export async function waitForMail(to: string, subjectContains: string, timeoutMs = 20_000): Promise<Mail> {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    const found = findMail(to, subjectContains);
    if (found) {
      consumed.add(found.file);
      return found.mail;
    }
    if (Date.now() > deadline) throw new Error(`No mail to ${to} with "${subjectContains}" within ${timeoutMs} ms (${MAIL_DIR}).`);
    await new Promise(r => setTimeout(r, 250));
  }
}

/** Waits `waitMs`, then fails if any unread mail to `to` with `subjectContains` arrived. */
export async function expectNoMail(to: string, subjectContains: string, waitMs = 5_000): Promise<void> {
  await new Promise(r => setTimeout(r, waitMs));
  expect(findMail(to, subjectContains), `unexpected mail to ${to}`).toBeUndefined();
}

function findMail(to: string, subjectContains: string): { file: string; mail: Mail } | undefined {
  if (!fs.existsSync(MAIL_DIR)) return undefined;
  const files = fs.readdirSync(MAIL_DIR).filter(f => f.endsWith('.eml')).sort();
  for (const name of files) {
    const file = path.join(MAIL_DIR, name);
    if (consumed.has(file) || fs.statSync(file).mtimeMs < loadedAt) continue;
    const part = parsePart(fs.readFileSync(file, 'latin1'));
    const subject = decodeWords(part.headers['subject'] ?? '');
    // Email:RedirectAllTo (Development) readdresses the mail and prefixes the subject with the original.
    const addressed = (part.headers['to'] ?? '').toLowerCase().includes(to.toLowerCase())
      || subject.toLowerCase().includes(`[to: ${to.toLowerCase()}]`);
    if (!addressed || !subject.includes(subjectContains)) continue;
    const text = textPart(part) ?? '';
    return { file, mail: { subject, text, link: text.match(/https:\/\/\S+/)?.[0] ?? null } };
  }
  return undefined;
}

interface Part { headers: Record<string, string>; body: string }

// Minimal MIME: enough for what MimeKit writes (multipart/alternative, quoted-printable or base64).
function parsePart(raw: string): Part {
  const split = raw.search(/\r?\n\r?\n/);
  const head = split < 0 ? raw : raw.slice(0, split);
  const body = split < 0 ? '' : raw.slice(split).replace(/^\r?\n\r?\n/, '');
  const headers: Record<string, string> = {};
  for (const line of head.replace(/\r?\n[ \t]+/g, ' ').split(/\r?\n/)) {
    const i = line.indexOf(':');
    if (i > 0) headers[line.slice(0, i).trim().toLowerCase()] = line.slice(i + 1).trim();
  }
  return { headers, body };
}

function textPart(part: Part): string | undefined {
  const type = (part.headers['content-type'] ?? 'text/plain').toLowerCase();
  if (type.startsWith('multipart/')) {
    const boundary = /boundary="?([^";]+)"?/i.exec(part.headers['content-type'])?.[1];
    if (!boundary) return undefined;
    for (const chunk of part.body.split(`--${boundary}`).slice(1)) {
      if (chunk.startsWith('--')) break;
      const text = textPart(parsePart(chunk.replace(/^\r?\n/, '')));
      if (text !== undefined) return text;
    }
    return undefined;
  }
  if (!type.startsWith('text/plain')) return undefined;
  return decodeBody(part.body, (part.headers['content-transfer-encoding'] ?? '7bit').toLowerCase());
}

function decodeBody(body: string, encoding: string): string {
  if (encoding === 'base64') return Buffer.from(body.replace(/\s+/g, ''), 'base64').toString('utf8');
  if (encoding === 'quoted-printable') {
    const bytes = body.replace(/=\r?\n/g, '').replace(/=([0-9A-F]{2})/gi, (_, h: string) => String.fromCharCode(parseInt(h, 16)));
    return Buffer.from(bytes, 'latin1').toString('utf8');
  }
  return Buffer.from(body, 'latin1').toString('utf8');
}

// RFC 2047 encoded words in headers (=?utf-8?B?...?= / =?utf-8?Q?...?=).
function decodeWords(value: string): string {
  return value
    .replace(/\?=\s+=\?/g, '?==?')
    .replace(/=\?([^?]+)\?([BQ])\?([^?]*)\?=/gi, (_, _charset: string, kind: string, data: string) =>
      kind.toUpperCase() === 'B'
        ? Buffer.from(data, 'base64').toString('utf8')
        : Buffer.from(data.replace(/_/g, ' ').replace(/=([0-9A-F]{2})/gi, (__, h: string) => String.fromCharCode(parseInt(h, 16))), 'latin1').toString('utf8'));
}
