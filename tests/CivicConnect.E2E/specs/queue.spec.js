const { test, expect } = require('@playwright/test');
const { queue, rows, titles, required } = require('./helpers');

const sortControl = page => page.getByLabel('Sort (date uses last updated)', { exact: true });
async function column(page, name) {
  const headers = await page.getByRole('table', { name: 'Tickets', exact: true }).getByRole('columnheader').allTextContents();
  const index = headers.indexOf(name);
  expect(index, `Missing visible ${name} column`).toBeGreaterThanOrEqual(0);
  return Promise.all((await rows(page).all()).map(row => row.getByRole('cell').nth(index).innerText()));
}

test('Staff authentication opens queue with all FR-15 controls', async ({ page }) => {
  await queue(page);
  await expect(page).toHaveURL(/\/staff\/queue$/);
  await expect(page.getByRole('searchbox', { name: 'Search tickets' })).toBeVisible();
  for (const name of ['Category', 'Status', 'Sort (date uses last updated)']) {
    await expect(page.getByRole('combobox', { name, exact: true })).toBeVisible();
  }
  await expect(page.getByRole('button', { name: 'Clear filters', exact: true })).toBeVisible();
});

test('Anonymous user cannot access the Staff Queue', async ({ page }) => {
  await page.goto('/staff/queue');
  await expect(page).toHaveURL(/\/login\?returnUrl=/);
  await expect(page.getByRole('heading', { name: 'Ticket Queue', exact: true })).toHaveCount(0);
  await expect(page.getByRole('table', { name: 'Tickets' })).toHaveCount(0);
});

test('FR-15 search retains a known match and removes a known nonmatch', async ({ page }) => {
  await queue(page);
  const match = required('E2E_SEARCH_MATCH_TITLE');
  const nonmatch = required('E2E_SEARCH_NONMATCH_TITLE');
  expect(match).not.toBe(nonmatch);
  await expect(rows(page).getByRole('link', { name: match, exact: true })).toBeVisible();
  await expect(rows(page).getByRole('link', { name: nonmatch, exact: true })).toBeVisible();
  await page.getByRole('searchbox', { name: 'Search tickets' }).fill(required('E2E_SEARCH_KEYWORD'));
  await expect(rows(page).getByRole('link', { name: match, exact: true })).toBeVisible();
  await expect(rows(page).getByRole('link', { name: nonmatch, exact: true })).toHaveCount(0);
});

for (const [name, variable] of [['Category', 'E2E_QUEUE_CATEGORY'], ['Status', 'E2E_QUEUE_STATUS']]) {
  test(`FR-15 configured ${name} filter`, async ({ page }) => {
    await queue(page);
    const value = required(variable);
    const baseline = await titles(page);
    const values = await column(page, name);
    const expected = baseline.filter((_, i) => values[i] === value);
    expect(expected.length, `${value} must have at least one visible fixture`).toBeGreaterThan(0);
    await page.getByRole('combobox', { name, exact: true }).selectOption({ label: value });
    await expect.poll(() => titles(page)).toEqual(expected);
    await expect.poll(() => column(page, name)).toEqual(expected.map(() => value));
    expect(expected.length, `Fixture limitation: ${name} filter needs a known nonmatching queue ticket`).toBeLessThan(baseline.length);
  });
}

test('FR-15 Clear filters restores the default queue and control values', async ({ page }) => {
  await queue(page);
  const baseline = await titles(page);
  await page.getByLabel('Category', { exact: true }).selectOption({ label: required('E2E_QUEUE_CATEGORY') });
  await page.getByLabel('Status', { exact: true }).selectOption({ label: required('E2E_QUEUE_STATUS') });
  await sortControl(page).selectOption('priority-low');
  await page.getByRole('searchbox', { name: 'Search tickets' }).fill('no-match-e2e-' + Date.now());
  await expect(page.getByRole('status')).toContainText('No tickets match');
  await page.getByRole('button', { name: 'Clear filters' }).click();
  await expect(page.getByRole('searchbox', { name: 'Search tickets' })).toHaveValue('');
  await expect(page.getByLabel('Category', { exact: true })).toHaveValue('');
  await expect(page.getByLabel('Status', { exact: true })).toHaveValue('');
  await expect(sortControl(page)).toHaveValue('date-newest');
  await expect.poll(() => titles(page)).toEqual(baseline);
});

for (const [sort, descending] of [['priority-high', true], ['priority-low', false]]) {
  test(`FR-15 sorting ${sort}`, async ({ page }) => {
    await queue(page);
    const ranks = { Low: 1, Medium: 2, High: 3, Critical: 4 };
    const read = async () => (await column(page, 'Priority')).map(text => {
      expect(Object.keys(ranks)).toContain(text);
      return ranks[text];
    });
    const baseline = await titles(page);
    const before = await read();
    expect(new Set(before).size, 'Fixture limitation: queue needs at least two distinct priorities to prove ordering').toBeGreaterThan(1);
    await sortControl(page).selectOption(sort);
    await expect.poll(read).toEqual([...before].sort((a, b) => descending ? b - a : a - b));
    await expect.poll(async () => (await titles(page)).sort()).toEqual([...baseline].sort());
  });
}

// Only absolute calendar dates are unambiguous without assuming the server's clock,
// timezone or relative-date parser. Keep this independent of production parsing code.
function absoluteDate(text) {
  if (/^\d{4}-\d{2}-\d{2}( \d{2}:\d{2}:\d{2})?$/.test(text)) {
    return Date.parse(text.replace(' ', 'T') + (text.includes(' ') ? 'Z' : 'T00:00:00Z'));
  }
  if (/^\d{1,2} [A-Z][a-z]{2} \d{4}$/.test(text)) return Date.parse(`${text} 00:00:00 GMT`);
  return NaN;
}
for (const [sort, descending] of [['date-newest', true], ['date-oldest', false]]) {
  test(`FR-15 sorting ${sort}`, async ({ page }) => {
    await queue(page);
    const values = await column(page, 'Last updated');
    const dates = values.map(absoluteDate);
    test.skip(dates.some(date => !Number.isFinite(date)), `Displayed dates include relative or unsupported values (${values.join('; ')}); server date/timezone is not exposed reliably.`);
    test.skip(new Set(dates).size < 2, 'At least two distinct absolute displayed dates are needed to prove date ordering.');
    const baseline = await titles(page);
    await sortControl(page).selectOption(sort);
    await expect.poll(async () => (await column(page, 'Last updated')).map(absoluteDate))
      .toEqual([...dates].sort((a, b) => descending ? b - a : a - b));
    await expect.poll(async () => (await titles(page)).sort()).toEqual([...baseline].sort());
  });
}
