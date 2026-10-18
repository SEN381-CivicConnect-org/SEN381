const { test, expect } = require('@playwright/test');
const { login, rows, required } = require('./helpers');

test('Requester creates a service request and sees it in My Tickets', async ({ page }) => {
  await login(page, 'REQUESTER_A');
  await page.getByRole('link', { name: 'New Ticket', exact: true }).click();
  const title = `E2E service request ${Date.now()}-${test.info().workerIndex}`;
  await page.getByLabel('Request title *', { exact: true }).fill(title);
  await page.getByLabel('Category *', { exact: true }).selectOption(process.env.E2E_CREATE_CATEGORY || 'Facilities');
  await page.getByLabel('Location *', { exact: true }).selectOption(process.env.E2E_CREATE_LOCATION || 'Ground Floor');
  await page.getByLabel('Description *', { exact: true }).fill(`Created by the browser E2E suite: ${title}`);
  await page.getByRole('button', { name: 'Submit Ticket', exact: true }).click();
  await expect(page).toHaveURL(/\/tickets\/CC-\d+$/);
  await expect(page.getByRole('heading', { name: title, exact: true })).toBeVisible();
  await page.getByRole('link', { name: 'My Tickets', exact: true }).click();
  await expect(rows(page).getByRole('link', { name: title, exact: true })).toBeVisible();
});

test('FR-11 Requester A can open their own request', async ({ page }) => {
  await login(page, 'REQUESTER_A');
  const title = required('E2E_REQUESTER_A_TICKET_TITLE');
  await rows(page).getByRole('link', { name: title, exact: true }).click();
  await expect(page).toHaveURL(url => url.pathname === `/tickets/${required('E2E_REQUESTER_A_TICKET_ID')}`);
  await expect(page.getByRole('heading', { name: title, exact: true })).toBeVisible();
});

test('FR-11 Requester A cannot see Requester B ticket in My Tickets', async ({ page }) => {
  await login(page, 'REQUESTER_A');
  await expect(page.getByRole('heading', { name: 'My Tickets', exact: true })).toBeVisible();
  await expect(rows(page).getByRole('link', { name: required('E2E_REQUESTER_B_TICKET_TITLE'), exact: true })).toHaveCount(0);
});

test('FR-11 Requester B owns the foreign ticket fixture', async ({ page }) => {
  await login(page, 'REQUESTER_B');
  await expect(rows(page).getByRole('link', { name: required('E2E_REQUESTER_B_TICKET_TITLE'), exact: true })).toBeVisible();
});

test('FR-11 direct route denies Requester A access to Requester B ticket', async ({ page }) => {
  // Login returnUrl reaches the direct detail route while keeping circuit-scoped identity.
  await login(page, 'REQUESTER_A', `/tickets/${required('E2E_REQUESTER_B_TICKET_ID')}`);
  await expect(page.getByRole('heading', { name: required('E2E_REQUESTER_B_TICKET_TITLE'), exact: true })).toHaveCount(0);
  await expect(page.getByRole('heading', { name: /access denied|ticket not found/i })).toBeVisible();
});

function distinctRequesters() {
  expect(required('E2E_REQUESTER_B_EMAIL').trim().toLowerCase(), 'Ownership needs two distinct Requester accounts')
    .not.toBe(required('E2E_REQUESTER_A_EMAIL').trim().toLowerCase());
}

test('FR-11 Requester B cannot navigate to Requester A request from My Tickets', async ({ page }) => {
  distinctRequesters();
  await login(page, 'REQUESTER_B');
  await expect(page.getByRole('heading', { name: 'My Tickets', exact: true })).toBeVisible();
  await expect(page.getByRole('table', { name: 'Tickets', exact: true })).toBeVisible();
  await expect(rows(page).getByRole('link', { name: required('E2E_REQUESTER_A_TICKET_TITLE'), exact: true })).toHaveCount(0);
});

test('FR-11 direct route denies Requester B access to Requester A request', async ({ page }) => {
  distinctRequesters();
  await login(page, 'REQUESTER_B', `/tickets/${required('E2E_REQUESTER_A_TICKET_ID')}`);
  await expect(page.getByRole('heading', { name: required('E2E_REQUESTER_A_TICKET_TITLE'), exact: true })).toHaveCount(0);
  await expect(page.getByRole('heading', { name: /access denied|ticket not found/i })).toBeVisible();
});
