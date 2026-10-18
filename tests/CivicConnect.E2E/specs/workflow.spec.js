const { test, expect } = require('@playwright/test');
const { queue, rows, required } = require('./helpers');

test('Staff performs a valid forward status transition', async ({ page }) => {
  await queue(page);
  const title = required('E2E_WORKFLOW_TICKET_TITLE');
  await rows(page).getByRole('link', { name: title, exact: true }).click();
  const status = page.getByLabel('Change status', { exact: true });
  await expect(status).toBeEnabled();
  const next = required('E2E_WORKFLOW_NEXT_STATUS');
  const option = status.getByRole('option', { name: next, exact: true });
  const label = await option.innerText();
  const value = await option.getAttribute('value');
  await status.selectOption({ label });
  await page.getByRole('button', { name: 'Update status', exact: true }).click();
  await expect(page.getByText(`Status: ${label}`, { exact: true })).toBeVisible();
  await expect(page.getByText(new RegExp(`→ ${value}$`))).toBeVisible();
});
