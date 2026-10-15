const { expect } = require('@playwright/test');

function required(name) {
  const value = process.env[name];
  if (!value) throw new Error(`Set ${name}; see tests/CivicConnect.E2E/README.md.`);
  return value;
}

async function login(page, role, returnUrl) {
  const email = required(`E2E_${role}_EMAIL`);
  const password = required(`E2E_${role}_PASSWORD`);
  // Inputs exist in prerendered HTML before their Blazor event handlers attach.
  // Wait for the initial server render batch on the app's default WebSocket
  // transport so a fast fill cannot be overwritten by circuit initialization.
  const interactive = page.waitForEvent('websocket', {
    predicate: socket => new URL(socket.url()).pathname.endsWith('/_blazor'),
  }).then(socket => socket.waitForEvent('framereceived', {
    predicate: frame => Buffer.from(frame.payload).includes(Buffer.from('JS.RenderBatch')),
  }));
  await page.goto(`/login${returnUrl ? `?returnUrl=${encodeURIComponent(returnUrl)}` : ''}`);
  await interactive;
  await page.getByLabel('Email address', { exact: true }).fill(email);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Sign out', exact: true })).toBeVisible();
}

async function queue(page) {
  await login(page, 'STAFF');
  await page.getByRole('link', { name: 'Ticket Queue', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Ticket Queue', exact: true })).toBeVisible();
  await expect(rows(page).first()).toBeVisible();
}

const rows = page => page.getByRole('table', { name: 'Tickets', exact: true })
  .getByRole('row').filter({ has: page.getByRole('cell') });
const titles = page => rows(page).getByRole('link').allTextContents();

module.exports = { required, login, queue, rows, titles };
