import { SCHEMAS, escapeHtml } from './schemas.js';

// Application State
const state = {
  currentCategory: 'functional_requirements',
  schemas: SCHEMAS,
  isServerConnected: false,
  directoryHandle: null,
  theme: localStorage.getItem('civic_doc_theme') || 'dark'
};

// DOM References
const elements = {
  categorySelect: document.getElementById('category-select'),
  docBadge: document.getElementById('doc-badge'),
  docPath: document.getElementById('doc-path'),
  docCount: document.getElementById('doc-count'),
  docPrefix: document.getElementById('doc-prefix'),
  nextIdDisplay: document.getElementById('next-id-display'),
  fieldsContainer: document.getElementById('fields-container'),
  form: document.getElementById('entry-form'),
  ticketRef: document.getElementById('ticket-ref'),
  createGithubIssue: document.getElementById('create-github-issue'),
  btnSubmit: document.getElementById('btn-submit'),
  btnPreview: document.getElementById('btn-preview'),
  btnCopy: document.getElementById('btn-copy'),
  btnReset: document.getElementById('btn-reset'),
  liveTableHead: document.getElementById('live-table-head'),
  liveTableBody: document.getElementById('live-table-body'),
  connectionStatus: document.getElementById('connection-status'),
  statusText: document.getElementById('status-text'),
  btnTheme: document.getElementById('btn-theme'),
  themeIcon: document.getElementById('theme-icon'),
  themeLabel: document.getElementById('theme-label'),
  previewModal: document.getElementById('preview-modal'),
  modalCode: document.getElementById('modal-code'),
  btnModalClose: document.getElementById('btn-modal-close'),
  btnModalCopy: document.getElementById('btn-modal-copy'),
  btnModalDone: document.getElementById('btn-modal-done'),
  toastContainer: document.getElementById('toast-container')
};

/**
 * Initialize theme
 */
function initTheme() {
  document.documentElement.setAttribute('data-theme', state.theme);
  updateThemeButton();

  elements.btnTheme.addEventListener('click', () => {
    state.theme = state.theme === 'dark' ? 'light' : 'dark';
    document.documentElement.setAttribute('data-theme', state.theme);
    localStorage.setItem('civic_doc_theme', state.theme);
    updateThemeButton();
  });
}

function updateThemeButton() {
  if (state.theme === 'dark') {
    elements.themeIcon.textContent = '☀️';
    elements.themeLabel.textContent = 'Light';
  } else {
    elements.themeIcon.textContent = '🌙';
    elements.themeLabel.textContent = 'Dark';
  }
}

/**
 * Toast Notification System
 */
function showToast(message, type = 'info') {
  const toast = document.createElement('div');
  toast.className = `toast ${type}`;
  toast.innerHTML = `<span>${escapeHtml(message)}</span>`;
  elements.toastContainer.appendChild(toast);

  setTimeout(() => {
    toast.style.transition = 'opacity 0.25s ease, transform 0.25s ease';
    toast.style.opacity = '0';
    toast.style.transform = 'translateY(8px)';
    setTimeout(() => toast.remove(), 250);
  }, 4000);
}

/**
 * Check connection to local Node.js server and sync live schema stats
 */
async function syncServerSchemas() {
  try {
    const res = await fetch('/api/schemas');
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const data = await res.json();
    if (data.success && data.schemas) {
      state.isServerConnected = true;
      for (const [key, serverSchema] of Object.entries(data.schemas)) {
        if (state.schemas[key]) {
          state.schemas[key].fileStatus = serverSchema.fileStatus;
        }
      }
      elements.connectionStatus.className = 'status-pill connected';
      elements.statusText.textContent = 'Server Connected';
      elements.statusText.title = 'Active local runner at /api/entry';
    }
  } catch {
    state.isServerConnected = false;
    elements.connectionStatus.className = 'status-pill standalone';
    elements.statusText.textContent = 'Standalone Mode';
    elements.statusText.title = 'Direct browser mode. Preview & copy enabled.';
  }
}

/**
 * Format local date as DD/MM/YYYY
 */
function getTodayFormatted() {
  const d = new Date();
  const day = String(d.getDate()).padStart(2, '0');
  const month = String(d.getMonth() + 1).padStart(2, '0');
  const year = d.getFullYear();
  return `${day}/${month}/${year}`;
}

/**
 * Render dynamic fields for the selected category
 */
function renderForm(categoryId) {
  const schema = state.schemas[categoryId] || SCHEMAS[categoryId];
  if (!schema) return;

  state.currentCategory = categoryId;
  const fileStatus = schema.fileStatus || {};

  // Update Category Meta
  elements.docBadge.textContent = schema.badge || schema.prefix || 'DOC';
  elements.docPath.textContent = schema.docFile;
  elements.docCount.textContent = fileStatus.rowCount !== undefined ? fileStatus.rowCount : '—';
  elements.docPrefix.textContent = schema.prefix ? `${schema.prefix}XX` : 'Custom';

  // Determine Next ID
  let calculatedNextId = fileStatus.nextId;
  if (!calculatedNextId && schema.prefix) {
    if (schema.prefix === 'DATE') {
      calculatedNextId = getTodayFormatted();
    } else {
      calculatedNextId = `${schema.prefix}01`;
    }
  }
  elements.nextIdDisplay.textContent = calculatedNextId || 'N/A';

  // Clear container
  elements.fieldsContainer.innerHTML = '';

  // Generate input elements
  schema.fields.forEach(field => {
    const group = document.createElement('div');
    group.className = 'form-group';

    const label = document.createElement('label');
    label.setAttribute('for', `f-${field.key}`);
    label.innerHTML = `
      <span>${escapeHtml(field.label)}${field.required ? ' <span class="req">*</span>' : ''}</span>
      ${field.hint ? `<span class="label-hint">${escapeHtml(field.hint)}</span>` : ''}
    `;
    group.appendChild(label);

    let inputEl;

    if (field.type === 'textarea') {
      inputEl = document.createElement('textarea');
      inputEl.id = `f-${field.key}`;
      inputEl.name = field.key;
      inputEl.rows = field.rows || 3;
      inputEl.placeholder = field.placeholder || '';
      if (field.required) inputEl.required = true;
    } else if (field.type === 'select') {
      inputEl = document.createElement('select');
      inputEl.id = `f-${field.key}`;
      inputEl.name = field.key;
      if (field.required) inputEl.required = true;

      field.options.forEach(opt => {
        const optEl = document.createElement('option');
        optEl.value = opt;
        optEl.textContent = opt;
        if (opt === field.default) optEl.selected = true;
        inputEl.appendChild(optEl);
      });
    } else if (field.type === 'combobox') {
      const wrapper = document.createElement('div');
      wrapper.style.display = 'flex';
      wrapper.style.gap = '0.5rem';

      inputEl = document.createElement('input');
      inputEl.type = 'text';
      inputEl.id = `f-${field.key}`;
      inputEl.name = field.key;
      inputEl.placeholder = field.placeholder || '';
      inputEl.setAttribute('list', `dl-${field.key}`);
      if (field.required) inputEl.required = true;

      const dataList = document.createElement('datalist');
      dataList.id = `dl-${field.key}`;
      field.options.forEach(opt => {
        const optEl = document.createElement('option');
        optEl.value = opt;
        dataList.appendChild(optEl);
      });

      wrapper.appendChild(inputEl);
      wrapper.appendChild(dataList);
      group.appendChild(wrapper);
    } else {
      inputEl = document.createElement('input');
      inputEl.type = 'text';
      inputEl.id = `f-${field.key}`;
      inputEl.name = field.key;
      inputEl.placeholder = field.placeholder || '';
      if (field.required) inputEl.required = true;

      if (field.auto) {
        inputEl.value = calculatedNextId || '';
        inputEl.classList.add('auto-id');
        inputEl.readOnly = true;

        // Add override toggle button
        const overrideBtn = document.createElement('button');
        overrideBtn.type = 'button';
        overrideBtn.className = 'icon-btn';
        overrideBtn.style.marginTop = '0.35rem';
        overrideBtn.style.alignSelf = 'flex-start';
        overrideBtn.innerHTML = '✏️ Edit ID manually';
        overrideBtn.addEventListener('click', () => {
          inputEl.readOnly = !inputEl.readOnly;
          overrideBtn.innerHTML = inputEl.readOnly ? '✏️ Edit ID manually' : '🔒 Lock Auto ID';
          if (!inputEl.readOnly) inputEl.focus();
        });
        group.appendChild(inputEl);
        group.appendChild(overrideBtn);
      }
    }

    if (!field.auto || field.type !== 'text') {
      if (field.type !== 'combobox') {
        group.appendChild(inputEl);
      }
    }

    elements.fieldsContainer.appendChild(group);
  });

  // Render live table preview
  renderLiveTable(schema);
}

/**
 * Render live preview of table headers and existing entries
 */
function renderLiveTable(schema) {
  elements.liveTableHead.innerHTML = '';
  elements.liveTableBody.innerHTML = '';

  const fileStatus = schema.fileStatus || {};
  const rows = fileStatus.rows || [];

  // Generate thead
  const theadTr = document.createElement('tr');
  schema.fields.forEach(f => {
    const th = document.createElement('th');
    th.textContent = f.label;
    if (f.key === 'id') th.className = 'id';
    theadTr.appendChild(th);
  });
  elements.liveTableHead.appendChild(theadTr);

  // Generate tbody
  if (rows.length === 0) {
    const tr = document.createElement('tr');
    const td = document.createElement('td');
    td.colSpan = schema.fields.length;
    td.className = 'empty-state';
    td.textContent = 'No rows discovered or file not yet loaded.';
    tr.appendChild(td);
    elements.liveTableBody.appendChild(tr);
    return;
  }

  rows.forEach(row => {
    const tr = document.createElement('tr');
    row.forEach(cell => {
      const td = document.createElement('td');
      td.textContent = cell.text;
      if (cell.attrs.includes('class="id"') || cell.attrs.includes("class='id'")) td.className = 'id';
      else if (cell.attrs.includes('class="name"')) td.className = 'name';
      else if (cell.attrs.includes('class="rating"')) td.className = 'rating';
      else if (cell.attrs.includes('class="todo"')) td.className = 'todo';
      tr.appendChild(td);
    });
    elements.liveTableBody.appendChild(tr);
  });
}

/**
 * Extract and validate form values
 */
function getFormData(validate = true) {
  const schema = SCHEMAS[state.currentCategory] || state.schemas[state.currentCategory];
  const data = {};
  let isValid = true;
  let firstInvalidEl = null;

  schema.fields.forEach(field => {
    const el = document.getElementById(`f-${field.key}`);
    if (!el) return;

    const val = el.value.trim();
    if (validate && field.required && !val) {
      el.classList.add('invalid');
      isValid = false;
      if (!firstInvalidEl) firstInvalidEl = el;
    } else if (validate) {
      el.classList.remove('invalid');
    }
    // If not validating and empty, display placeholder preview
    data[field.key] = val || (validate ? '' : `[${field.label}]`);
  });

  if (validate && !isValid) {
    if (firstInvalidEl) firstInvalidEl.focus();
    showToast('Please complete all required fields before proceeding.', 'err');
    return null;
  }

  return data;
}

/**
 * Handle form submission
 */
async function handleSubmit(e) {
  e.preventDefault();
  const formData = getFormData(true);
  if (!formData) return;

  const schema = SCHEMAS[state.currentCategory];
  const ticketRef = elements.ticketRef.value.trim();
  const createIssue = elements.createGithubIssue.checked;

  elements.btnSubmit.disabled = true;
  elements.btnSubmit.innerHTML = '<span>⏳ Saving to document...</span>';

  if (state.isServerConnected) {
    try {
      const response = await fetch('/api/entry', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          categoryId: state.currentCategory,
          data: formData,
          ticketRef,
          createIssue
        })
      });

      const result = await response.json();
      if (!response.ok || !result.success) {
        throw new Error(result.error || 'Server returned error');
      }

      showToast(`Added successfully to ${schema.docFile}!`, 'ok');

      if (result.issueResult && result.issueResult.success) {
        showToast(`GitHub issue created: ${result.issueResult.url}`, 'ok');
      } else if (result.issueResult && !result.issueResult.success) {
        showToast(`Doc updated, but GitHub CLI issue failed: ${result.issueResult.suggestion}`, 'info');
      }

      // Re-fetch updated schemas and reload form
      await syncServerSchemas();
      resetNonIdInputs();
      renderForm(state.currentCategory);
    } catch (err) {
      showToast(`Failed to update file: ${err.message}`, 'err');
    } finally {
      elements.btnSubmit.disabled = false;
      elements.btnSubmit.innerHTML = '<span>⚡ Produce & Save to Document</span>';
    }
  } else {
    // Standalone mode: Show generated HTML modal drawer
    elements.btnSubmit.disabled = false;
    elements.btnSubmit.innerHTML = '<span>⚡ Produce & Save to Document</span>';
    openPreviewModal(formData);
    showToast('Running in standalone mode. Copy the generated HTML below.', 'info');
  }
}

/**
 * Reset form input fields except auto IDs
 */
function resetNonIdInputs() {
  const schema = SCHEMAS[state.currentCategory] || state.schemas[state.currentCategory];
  schema.fields.forEach(field => {
    if (!field.auto) {
      const el = document.getElementById(`f-${field.key}`);
      if (el) {
        el.value = field.default || '';
        el.classList.remove('invalid');
      }
    }
  });
  elements.ticketRef.value = '';
  elements.createGithubIssue.checked = false;
}

/**
 * Modal Drawer for Preview and Copy
 */
function openPreviewModal(data = null) {
  try {
    const schema = SCHEMAS[state.currentCategory];
    const payload = data || getFormData(false) || {};
    const rowHtml = schema.renderRow(payload);

    elements.modalCode.textContent = rowHtml;
    elements.previewModal.classList.add('open');
    elements.previewModal.setAttribute('aria-hidden', 'false');
  } catch (err) {
    console.error('Error in openPreviewModal:', err);
    showToast(`Could not generate preview: ${err.message}`, 'err');
  }
}

function closePreviewModal() {
  elements.previewModal.classList.remove('open');
  elements.previewModal.setAttribute('aria-hidden', 'true');
}

/**
 * Copy HTML row to clipboard
 */
async function copyRowHtml() {
  try {
    const schema = SCHEMAS[state.currentCategory];
    const data = getFormData(false);
    if (!data) return;

    const rowHtml = schema.renderRow(data);

    if (navigator.clipboard && navigator.clipboard.writeText) {
      try {
        await navigator.clipboard.writeText(rowHtml);
        showToast('HTML row block copied to clipboard!', 'ok');
        return;
      } catch (clipErr) {
        console.warn('Clipboard writeText failed, using fallback:', clipErr);
      }
    }

    // Fallback using temporary textarea
    const ta = document.createElement('textarea');
    ta.value = rowHtml;
    ta.style.position = 'fixed';
    ta.style.left = '-9999px';
    ta.style.top = '0';
    document.body.appendChild(ta);
    ta.focus();
    ta.select();
    const copied = document.execCommand('copy');
    ta.remove();

    if (copied) {
      showToast('HTML row block copied to clipboard!', 'ok');
    } else {
      openPreviewModal(data);
      showToast('Clipboard write denied. Opening preview to copy manually.', 'info');
    }
  } catch (err) {
    console.error('Error in copyRowHtml:', err);
    showToast(`Could not copy HTML: ${err.message}`, 'err');
  }
}

/**
 * Event Listeners
 */
elements.categorySelect.addEventListener('change', (e) => {
  renderForm(e.target.value);
});

elements.form.addEventListener('submit', handleSubmit);

elements.btnPreview.addEventListener('click', () => {
  openPreviewModal();
});

elements.btnCopy.addEventListener('click', copyRowHtml);

elements.btnReset.addEventListener('click', () => {
  resetNonIdInputs();
  showToast('Form inputs cleared.', 'info');
});

elements.btnModalClose.addEventListener('click', closePreviewModal);
elements.btnModalDone.addEventListener('click', closePreviewModal);

elements.btnModalCopy.addEventListener('click', async () => {
  const code = elements.modalCode.textContent;
  try {
    if (navigator.clipboard && navigator.clipboard.writeText) {
      await navigator.clipboard.writeText(code);
      showToast('HTML row block copied to clipboard!', 'ok');
      return;
    }
    throw new Error('Clipboard API unavailable');
  } catch {
    const ta = document.createElement('textarea');
    ta.value = code;
    ta.style.position = 'fixed';
    ta.style.left = '-9999px';
    ta.style.top = '0';
    document.body.appendChild(ta);
    ta.focus();
    ta.select();
    document.execCommand('copy');
    ta.remove();
    showToast('HTML row block copied to clipboard!', 'ok');
  }
});

// Close modal on click outside
elements.previewModal.addEventListener('click', (e) => {
  if (e.target === elements.previewModal) {
    closePreviewModal();
  }
});

// Escape key to close modal
document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape' && elements.previewModal.classList.contains('open')) {
    closePreviewModal();
  }
});

/**
 * Bootstrap Application
 */
async function init() {
  initTheme();
  await syncServerSchemas();
  renderForm(state.currentCategory);
}

init();
