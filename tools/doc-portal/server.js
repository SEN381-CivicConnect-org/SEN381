import http from 'http';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';
import { exec } from 'child_process';
import { promisify } from 'util';
import { SCHEMAS, escapeHtml } from './public/schemas.js';

const execAsync = promisify(exec);
const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Resolve documentation root directory (SEN381/docs)
const DOCS_DIR = path.resolve(__dirname, '../../docs');
const PUBLIC_DIR = path.resolve(__dirname, 'public');
const PORT = process.env.PORT || 3381;

// MIME types dictionary for static file serving
const MIME_TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.js': 'application/javascript; charset=utf-8',
  '.mjs': 'application/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon'
};

/**
 * Scan target HTML file for highest ID and extract existing rows
 */
function inspectDocFile(relativePath, prefix, padLength = 2) {
  const fullPath = path.join(DOCS_DIR, relativePath);
  if (!fs.existsSync(fullPath)) {
    return { exists: false, error: 'File not found', nextId: null, rows: [] };
  }

  const content = fs.readFileSync(fullPath, 'utf8');

  // Extract next ID if prefix provided
  let nextId = null;
  let maxIdNum = 0;
  if (prefix === 'DATE') {
    const d = new Date();
    const day = String(d.getDate()).padStart(2, '0');
    const month = String(d.getMonth() + 1).padStart(2, '0');
    const year = d.getFullYear();
    nextId = `${day}/${month}/${year}`;
  } else if (prefix) {
    const escapedPrefix = prefix.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const regex = new RegExp(`${escapedPrefix}(\\d+)`, 'g');
    let match;
    while ((match = regex.exec(content)) !== null) {
      const num = parseInt(match[1], 10);
      if (num > maxIdNum) maxIdNum = num;
    }
    nextId = `${prefix}${String(maxIdNum + 1).padStart(padLength, '0')}`;
  }

  // Extract table rows
  const rows = [];
  const tbodyMatch = content.match(/<tbody[\s\S]*?<\/tbody>/i);
  if (tbodyMatch) {
    const trMatches = tbodyMatch[0].match(/<tr[\s\S]*?<\/tr>/gi) || [];
    for (const tr of trMatches) {
      const cells = [...tr.matchAll(/<td([^>]*)>([\s\S]*?)<\/td>/gi)].map(m => {
        return {
          attrs: m[1].trim(),
          text: m[2].replace(/<[^>]+>/g, '').trim()
        };
      });
      if (cells.length > 0) {
        rows.push(cells);
      }
    }
  }

  return {
    exists: true,
    maxIdNum,
    nextId,
    rowCount: rows.length,
    rows: rows.slice(-15) // Most recent rows
  };
}

/**
 * Append formatted row into target document's tbody with precise indentation
 */
function insertRowIntoFile(relativePath, rowHtml) {
  const fullPath = path.join(DOCS_DIR, relativePath);
  if (!fs.existsSync(fullPath)) {
    throw new Error(`Target document not found at: ${fullPath}`);
  }

  const content = fs.readFileSync(fullPath, 'utf8');

  // Locate the closing </tbody> tag
  const tbodyCloseRegex = /([ \t]*)<\/tbody>/i;
  const match = content.match(tbodyCloseRegex);
  if (!match) {
    throw new Error(`Could not find closing </tbody> in: ${relativePath}`);
  }

  const baseIndent = match[1] || '    ';
  const rowIndent = baseIndent + '  ';
  const cellIndent = rowIndent + '  ';

  // Format the row lines cleanly
  const lines = rowHtml.trim().split(/\r?\n/).map(l => l.trim()).filter(Boolean);
  let indentedRow = '';
  for (const line of lines) {
    if (line.startsWith('<tr') || line.startsWith('</tr')) {
      indentedRow += `${rowIndent}${line}\n`;
    } else {
      indentedRow += `${cellIndent}${line}\n`;
    }
  }

  const insertIndex = match.index;
  const updatedContent = content.slice(0, insertIndex) +
    indentedRow +
    baseIndent +
    '</tbody>' +
    content.slice(insertIndex + match[0].length);

  fs.writeFileSync(fullPath, updatedContent, 'utf8');
  return true;
}

/**
 * Optionally create GitHub issue via gh CLI
 */
async function createGitHubIssue(title, body, labels = []) {
  try {
    const labelArg = labels.length ? ` --label "${labels.join(',')}"` : '';
    // Escape double quotes and backslashes in body and title
    const safeTitle = title.replace(/"/g, '\\"');
    const safeBody = body.replace(/"/g, '\\"').replace(/`/g, '\\`');

    const cmd = `gh issue create --title "${safeTitle}" --body "${safeBody}"${labelArg}`;
    const { stdout, stderr } = await execAsync(cmd, { cwd: path.resolve(__dirname, '../..') });
    return { success: true, url: stdout.trim() };
  } catch (err) {
    return {
      success: false,
      error: err.message,
      suggestion: 'Ensure GitHub CLI is authenticated (`gh auth login`). The document update still succeeded.'
    };
  }
}

/**
 * Request dispatcher
 */
const server = http.createServer(async (req, res) => {
  // CORS Headers
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type');

  if (req.method === 'OPTIONS') {
    res.writeHead(204);
    res.end();
    return;
  }

  const parsedUrl = new URL(req.url, `http://${req.headers.host || 'localhost'}`);
  const pathname = parsedUrl.pathname;

  // API Route: GET /api/schemas
  if (req.method === 'GET' && pathname === '/api/schemas') {
    try {
      const liveSchemas = {};
      for (const [key, schema] of Object.entries(SCHEMAS)) {
        const fileInfo = inspectDocFile(schema.docFile, schema.prefix, schema.padLength);
        liveSchemas[key] = {
          ...schema,
          fileStatus: fileInfo
        };
      }
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ success: true, schemas: liveSchemas, docsDir: DOCS_DIR }));
    } catch (err) {
      res.writeHead(500, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ success: false, error: err.message }));
    }
    return;
  }

  // API Route: POST /api/entry
  if (req.method === 'POST' && pathname === '/api/entry') {
    let body = '';
    req.on('data', chunk => { body += chunk; });
    req.on('end', async () => {
      try {
        const payload = JSON.parse(body);
        const { categoryId, data, ticketRef, createIssue } = payload;

        const schema = SCHEMAS[categoryId];
        if (!schema) {
          res.writeHead(400, { 'Content-Type': 'application/json' });
          res.end(JSON.stringify({ success: false, error: `Invalid category: ${categoryId}` }));
          return;
        }

        // Generate the formatted HTML row
        const rowHtml = schema.renderRow(data);

        // Write row into file
        insertRowIntoFile(schema.docFile, rowHtml);

        // Re-read file to get updated next ID
        const fileInfo = inspectDocFile(schema.docFile, schema.prefix, schema.padLength);

        // Optionally create GitHub issue
        let issueResult = null;
        if (createIssue) {
          const issueTitle = `[${schema.badge || schema.title}] ${data.id || data.requirement || data.conflict || 'New Spec Entry'}`;
          const issueBody = `### Documentation Entry Added\n\n` +
            `**Category:** ${schema.title}\n` +
            `**Target File:** \`${schema.docFile}\`\n` +
            `**Ticket / Ref:** ${ticketRef || 'None'}\n\n` +
            `\`\`\`html\n${rowHtml}\n\`\`\`\n\n` +
            `*Created automatically via CivicConnect Documentation Portal.*`;
          issueResult = await createGitHubIssue(issueTitle, issueBody, [categoryId, 'documentation']);
        }

        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({
          success: true,
          rowHtml,
          updatedInfo: fileInfo,
          issueResult,
          message: `Successfully added to ${schema.docFile}`
        }));
      } catch (err) {
        res.writeHead(500, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ success: false, error: err.message }));
      }
    });
    return;
  }

  // API Route: POST /api/github/issue
  if (req.method === 'POST' && pathname === '/api/github/issue') {
    let body = '';
    req.on('data', chunk => { body += chunk; });
    req.on('end', async () => {
      try {
        const { title, body: issueBody, labels } = JSON.parse(body);
        const result = await createGitHubIssue(title, issueBody, labels);
        res.writeHead(result.success ? 200 : 400, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify(result));
      } catch (err) {
        res.writeHead(500, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ success: false, error: err.message }));
      }
    });
    return;
  }

  // Static File Serving
  let relativeFilePath = pathname === '/' ? 'index.html' : pathname.replace(/^\//, '');
  const safePath = path.normalize(relativeFilePath).replace(/^(\.\.[\/\\])+/, '');
  const filePath = path.join(PUBLIC_DIR, safePath);

  if (fs.existsSync(filePath) && fs.statSync(filePath).isFile()) {
    const ext = path.extname(filePath).toLowerCase();
    const contentType = MIME_TYPES[ext] || 'application/octet-stream';
    res.writeHead(200, { 'Content-Type': contentType });
    fs.createReadStream(filePath).pipe(res);
  } else {
    res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
    res.end('404 Not Found');
  }
});

server.on('error', (err) => {
  if (err.code === 'EADDRINUSE') {
    console.error(`\n[!] Error: Port ${PORT} is already in use.`);
    console.error(`    Another instance of the Documentation Portal is likely already running.`);
    console.error(`    You can either stop the process using port ${PORT} or specify a custom port:`);
    console.error(`    $env:PORT=3382; node tools/doc-portal/server.js\n`);
  } else {
    console.error('Server error:', err);
  }
  process.exit(1);
});

server.listen(PORT, () => {
  console.log(`====================================================`);
  console.log(`  SEN381 CivicConnect Documentation Portal running!`);
  console.log(`  Local URL:   http://localhost:${PORT}`);
  console.log(`  Docs Path:   ${DOCS_DIR}`);
  console.log(`====================================================`);
});
