const { test, expect } = require('@playwright/test');
const { login, queue } = require('./helpers');

// Same-document browser navigation preserves the current Blazor circuit identity.
// A full page.goto would start a new anonymous circuit and cannot test role denial.
async function visitInCircuit(page, path) {
  await page.evaluate(path => {
    history.pushState(null, '', path);
    dispatchEvent(new PopStateEvent('popstate'));
  }, path);
}

async function logout(page) {
  await page.getByRole('button', { name: 'Sign out', exact: true }).click();
  await expect(page).toHaveURL(/\/login$/);
  await visitInCircuit(page, '/staff/queue');
  await expect(page).toHaveURL(/\/login\?returnUrl=/);
  await expect(page.getByRole('heading', { name: 'Ticket Queue', exact: true })).toHaveCount(0);
}

test('Staff can use queue and logout revokes access', async ({ page }) => {
  await queue(page);
  await logout(page);
});

test('Requester login works but Staff Queue is denied', async ({ page }) => {
  await login(page, 'REQUESTER_A');
  await expect(page.getByRole('heading', { name: 'My Tickets', exact: true })).toBeVisible();
  await visitInCircuit(page, '/staff/queue');
  await expect(page).toHaveURL(/\/access-denied\?returnUrl=/);
  await expect(page.getByRole('heading', { name: 'Access denied', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Ticket Queue', exact: true })).toHaveCount(0);
  await logout(page);
});

test('Management retains its workspace and cannot enter Staff Queue', async ({ page }) => {
  await login(page, 'MANAGEMENT');
  await expect(page.getByRole('heading', { name: 'Management Dashboard', exact: true })).toBeVisible();
  await page.getByRole('link', { name: 'All Tickets', exact: true }).click();
  await expect(page).toHaveURL(/\/management\/tickets$/);
  await visitInCircuit(page, '/staff/queue');
  await expect(page.getByRole('heading', { name: 'Access denied', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Return to my workspace', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Management Dashboard', exact: true })).toBeVisible();
  await logout(page);
});
