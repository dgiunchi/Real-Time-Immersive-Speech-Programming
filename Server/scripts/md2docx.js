// Markdown -> .docx for the Say It Again design document.
//
// Written rather than reached for because pandoc, LibreOffice and every other
// converter are absent on this machine. Scope is deliberately the subset this
// document actually uses: ATX headings, paragraphs, pipe tables, blockquotes,
// bullet and ordered lists, horizontal rules, and inline bold/italic/code/link.
// Anything outside that is passed through as plain text rather than silently
// dropped, so a future edit to the source cannot quietly lose content.

const fs = require("fs");
const path = require("path");
const {
  Document, Packer, Paragraph, TextRun, HeadingLevel, Table, TableRow, TableCell,
  WidthType, BorderStyle, ShadingType, AlignmentType, ExternalHyperlink,
  LevelFormat, PageOrientation
} = require("docx");

const SRC = process.argv[2];
const OUT = process.argv[3];
if (!SRC || !OUT) {
  console.error("usage: node md2docx.js <input.md> <output.docx>");
  process.exit(1);
}

// A4 portrait in DXA (1440 = 1 inch). The content width below must match this
// minus the margins, or tables overflow the page.
const PAGE_W = 11906, MARGIN = 1080;
const CONTENT_W = PAGE_W - MARGIN * 2;

const FONT = "Calibri";
const MONO = "Consolas";
const ACCENT = "1F4E79";
const MUTED  = "595959";

// ── Inline formatting ───────────────────────────────────────────────────────
// One pass, longest-token-first, so `**bold**` is not seen as two `*italic*`
// markers. Returns TextRun/ExternalHyperlink children for a Paragraph.
function inline(text, base = {}) {
  const out = [];
  // Order matters: links first (they contain other punctuation), then code,
  // then bold, then italic.
  const re = /\[([^\]]+)\]\(([^)]+)\)|`([^`]+)`|\*\*([^*]+)\*\*|\*([^*]+)\*|__([^_]+)__/g;
  let last = 0, m;

  const push = (t, extra) => {
    if (!t) return;
    out.push(new TextRun({ text: t, font: FONT, size: 21, ...base, ...extra }));
  };

  while ((m = re.exec(text)) !== null) {
    push(decode(text.slice(last, m.index)));
    if (m[1] !== undefined) {
      out.push(new ExternalHyperlink({
        link: m[2],
        children: [new TextRun({
          text: decode(m[1]), font: FONT, size: 21, color: ACCENT,
          underline: {}, ...base
        })]
      }));
    } else if (m[3] !== undefined) {
      push(decode(m[3]), { font: MONO, size: 19, color: "A31515" });
    } else if (m[4] !== undefined) {
      push(decode(m[4]), { bold: true });
    } else if (m[5] !== undefined) {
      push(decode(m[5]), { italics: true });
    } else if (m[6] !== undefined) {
      push(decode(m[6]), { bold: true });
    }
    last = re.lastIndex;
  }
  push(decode(text.slice(last)));
  return out.length ? out : [new TextRun({ text: "", font: FONT, size: 21 })];
}

// The source is written for GitHub, so it carries HTML entities in places.
function decode(s) {
  return s
    .replace(/&mdash;/g, "—").replace(/&ndash;/g, "–")
    .replace(/&rsquo;/g, "’").replace(/&lsquo;/g, "‘")
    .replace(/&ldquo;/g, "“").replace(/&rdquo;/g, "”")
    .replace(/&hellip;/g, "…").replace(/&amp;/g, "&")
    .replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&nbsp;/g, " ");
}

// ── Table ───────────────────────────────────────────────────────────────────
function makeTable(rows) {
  const header = rows[0];
  const cols = header.length;
  // Both the table and every cell need an explicit DXA width, or Google Docs
  // and Word disagree about the layout.
  const colW = Math.floor(CONTENT_W / cols);
  const widths = Array(cols).fill(colW);
  widths[cols - 1] = CONTENT_W - colW * (cols - 1);

  const border = { style: BorderStyle.SINGLE, size: 4, color: "BFBFBF" };

  const mkRow = (cells, isHeader) => new TableRow({
    tableHeader: isHeader,
    children: cells.map((c, i) => new TableCell({
      width: { size: widths[i], type: WidthType.DXA },
      shading: isHeader
        ? { type: ShadingType.CLEAR, fill: "EDF2F7" }
        : undefined,
      margins: { top: 60, bottom: 60, left: 110, right: 110 },
      children: [new Paragraph({
        spacing: { before: 20, after: 20 },
        children: inline(c, isHeader ? { bold: true } : {})
      })]
    }))
  });

  return new Table({
    width: { size: CONTENT_W, type: WidthType.DXA },
    columnWidths: widths,
    borders: {
      top: border, bottom: border, left: border, right: border,
      insideHorizontal: border, insideVertical: border
    },
    rows: [
      mkRow(header, true),
      ...rows.slice(1).map(r => mkRow(r, false))
    ]
  });
}

function splitRow(line) {
  return line.replace(/^\s*\|/, "").replace(/\|\s*$/, "")
             .split("|").map(s => s.trim());
}

// ── Document build ──────────────────────────────────────────────────────────
const lines = fs.readFileSync(SRC, "utf8").split("\n");
const children = [];
let i = 0;

const HEADING = {
  1: HeadingLevel.HEADING_1,
  2: HeadingLevel.HEADING_2,
  3: HeadingLevel.HEADING_3,
  4: HeadingLevel.HEADING_4
};

while (i < lines.length) {
  const line = lines[i];
  const t = line.trim();

  if (!t) { i++; continue; }

  // Horizontal rule -> a ruled empty paragraph, never a one-row table.
  if (/^(-{3,}|\*{3,}|_{3,})$/.test(t)) {
    children.push(new Paragraph({
      spacing: { before: 120, after: 120 },
      border: { bottom: { style: BorderStyle.SINGLE, size: 6, color: "D0D7DE" } },
      children: [new TextRun("")]
    }));
    i++; continue;
  }

  // Heading
  const h = t.match(/^(#{1,4})\s+(.*)$/);
  if (h) {
    const level = h[1].length;
    children.push(new Paragraph({
      heading: HEADING[level],
      spacing: { before: level === 1 ? 0 : 260, after: 120 },
      children: inline(h[2], { color: level <= 2 ? ACCENT : "2F5597", bold: true })
    }));
    i++; continue;
  }

  // Pipe table: a header row followed by a |---|---| separator.
  if (t.startsWith("|") && i + 1 < lines.length &&
      /^\s*\|[\s:|-]+\|\s*$/.test(lines[i + 1])) {
    const rows = [splitRow(t)];
    i += 2;
    while (i < lines.length && lines[i].trim().startsWith("|")) {
      rows.push(splitRow(lines[i].trim()));
      i++;
    }
    children.push(makeTable(rows));
    children.push(new Paragraph({ spacing: { after: 120 }, children: [new TextRun("")] }));
    continue;
  }

  // Blockquote — collected as a block so a multi-line quote stays one unit.
  if (t.startsWith(">")) {
    const buf = [];
    while (i < lines.length && lines[i].trim().startsWith(">")) {
      buf.push(lines[i].trim().replace(/^>\s?/, ""));
      i++;
    }
    // Blank lines inside the quote separate paragraphs.
    const paras = buf.join("\n").split(/\n\s*\n/);
    paras.forEach(p => {
      children.push(new Paragraph({
        spacing: { before: 80, after: 80 },
        indent: { left: 340 },
        border: { left: { style: BorderStyle.SINGLE, size: 12, color: ACCENT, space: 10 } },
        children: inline(p.replace(/\n/g, " ").trim(), { italics: true, color: "24292F" })
      }));
    });
    continue;
  }

  // Bullet list
  if (/^[-*+]\s+/.test(t)) {
    const buf = [];
    while (i < lines.length) {
      const cur = lines[i];
      if (/^\s*[-*+]\s+/.test(cur)) {
        buf.push(cur.replace(/^\s*[-*+]\s+/, ""));
        i++;
      } else if (cur.trim() && /^\s{2,}\S/.test(cur) && buf.length) {
        // continuation of the previous bullet
        buf[buf.length - 1] += " " + cur.trim();
        i++;
      } else break;
    }
    buf.forEach(b => children.push(new Paragraph({
      bullet: { level: 0 },
      spacing: { before: 40, after: 40 },
      children: inline(b)
    })));
    continue;
  }

  // Ordered list
  if (/^\d+\.\s+/.test(t)) {
    const buf = [];
    while (i < lines.length) {
      const cur = lines[i];
      if (/^\s*\d+\.\s+/.test(cur)) {
        buf.push(cur.replace(/^\s*\d+\.\s+/, ""));
        i++;
      } else if (cur.trim() && /^\s{2,}\S/.test(cur) && buf.length) {
        buf[buf.length - 1] += " " + cur.trim();
        i++;
      } else break;
    }
    buf.forEach(b => children.push(new Paragraph({
      numbering: { reference: "ordered", level: 0 },
      spacing: { before: 40, after: 40 },
      children: inline(b)
    })));
    continue;
  }

  // Paragraph: join wrapped lines until a blank line or a block marker.
  const buf = [];
  while (i < lines.length) {
    const cur = lines[i];
    const ct = cur.trim();
    if (!ct) break;
    if (/^(#{1,4})\s/.test(ct) || ct.startsWith("|") || ct.startsWith(">") ||
        /^[-*+]\s/.test(ct) || /^\d+\.\s/.test(ct) ||
        /^(-{3,}|\*{3,}|_{3,})$/.test(ct)) break;
    buf.push(ct);
    i++;
  }
  if (buf.length) {
    children.push(new Paragraph({
      spacing: { before: 60, after: 120, line: 276 },
      children: inline(buf.join(" "))
    }));
  }
}

const doc = new Document({
  creator: "Akbar Juraev",
  title: "Say It Again — Study Design",
  description: "Study design, hypotheses, procedure and panel instrument",
  numbering: {
    config: [{
      reference: "ordered",
      levels: [{
        level: 0, format: LevelFormat.DECIMAL, text: "%1.",
        alignment: AlignmentType.START,
        style: { paragraph: { indent: { left: 620, hanging: 300 } } }
      }]
    }]
  },
  sections: [{
    properties: {
      page: {
        size: { width: PAGE_W, height: 16838, orientation: PageOrientation.PORTRAIT },
        margin: { top: MARGIN, right: MARGIN, bottom: MARGIN, left: MARGIN }
      }
    },
    children
  }]
});

Packer.toBuffer(doc).then(buf => {
  fs.writeFileSync(OUT, buf);
  console.log(`wrote ${OUT} (${(buf.length / 1024).toFixed(0)} KB, ${children.length} blocks)`);
});
