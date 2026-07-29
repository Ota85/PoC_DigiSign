"""Generate the DigiSign PoC document-signing user manual.

The editable content lives in docs/signing-user-guide.md. This renderer applies
the controlled visual layout used for the distributable PDF.
"""

from __future__ import annotations

import html
import os
import re
from pathlib import Path

from pypdf import PdfReader
from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.platypus import (
    BaseDocTemplate,
    Frame,
    KeepTogether,
    ListFlowable,
    ListItem,
    NextPageTemplate,
    PageBreak,
    PageTemplate,
    Paragraph,
    Preformatted,
    Spacer,
    Table,
    TableStyle,
)
from reportlab.platypus.tableofcontents import TableOfContents


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "docs" / "signing-user-guide.md"
OUTPUT = ROOT / "output" / "pdf" / "DigiSign-PoC-Signing-User-Manual.pdf"
TEMP_DIR = ROOT / "tmp" / "pdfs" / "signing-manual"
TEMP_PDF = TEMP_DIR / "DigiSign-PoC-Signing-User-Manual.pdf"

PAGE_WIDTH, PAGE_HEIGHT = A4
LEFT = 21 * mm
RIGHT = 21 * mm
TOP = 23 * mm
BOTTOM = 20 * mm

NAVY = colors.HexColor("#173E5E")
BLUE = colors.HexColor("#0879BE")
PALE_BLUE = colors.HexColor("#DCEEF8")
VERY_PALE_BLUE = colors.HexColor("#F2F8FC")
INK = colors.HexColor("#243441")
MUTED = colors.HexColor("#5E7080")
LINE = colors.HexColor("#B9C9D4")
LIGHT = colors.HexColor("#F2F5F7")
WARNING_BG = colors.HexColor("#FFF1EE")
WARNING_BORDER = colors.HexColor("#D84632")
SUCCESS_BG = colors.HexColor("#EDF8F1")
SUCCESS_BORDER = colors.HexColor("#2B8A57")


def ascii_safe(value: str) -> str:
    """Normalize punctuation unsupported by the built-in PDF fonts."""

    replacements = {
        "\u2010": "-",
        "\u2011": "-",
        "\u2012": "-",
        "\u2013": "-",
        "\u2014": "-",
        "\u2212": "-",
        "\u2018": "'",
        "\u2019": "'",
        "\u201c": '"',
        "\u201d": '"',
        "\u2192": "->",
        "\u2193": "v",
        "\u00d7": "x",
        "\u00a0": " ",
        "\u2500": "-",
        "\u2502": "|",
        "\u250c": "+",
        "\u2510": "+",
        "\u2514": "+",
        "\u2518": "+",
        "\u251c": "+",
        "\u2524": "+",
        "\u252c": "+",
        "\u2534": "+",
        "\u253c": "+",
    }
    for source, replacement in replacements.items():
        value = value.replace(source, replacement)
    return value


def inline_markup(value: str) -> str:
    value = ascii_safe(value.strip())
    escaped = html.escape(value, quote=True)
    escaped = re.sub(
        r"\[([^\]]+)\]\((https?://[^)]+)\)",
        lambda match: (
            f'<link href="{html.escape(match.group(2), quote=True)}" '
            f'color="#0879BE"><u>{match.group(1)}</u></link>'
        ),
        escaped,
    )
    escaped = re.sub(r"`([^`]+)`", r'<font name="Courier">\1</font>', escaped)
    escaped = re.sub(r"\*\*([^*]+)\*\*", r"<b>\1</b>", escaped)
    return escaped


def build_styles():
    sample = getSampleStyleSheet()
    return {
        "body": ParagraphStyle(
            "ManualBody",
            parent=sample["BodyText"],
            fontName="Helvetica",
            fontSize=9.1,
            leading=12.4,
            textColor=INK,
            spaceAfter=5.5,
            allowWidows=0,
            allowOrphans=0,
        ),
        "small": ParagraphStyle(
            "ManualSmall",
            parent=sample["BodyText"],
            fontName="Helvetica",
            fontSize=7.7,
            leading=10.1,
            textColor=MUTED,
        ),
        "table_header": ParagraphStyle(
            "ManualTableHeader",
            parent=sample["BodyText"],
            fontName="Helvetica-Bold",
            fontSize=7.5,
            leading=9.5,
            textColor=colors.white,
        ),
        "stage": ParagraphStyle(
            "ManualStage",
            parent=sample["BodyText"],
            fontName="Helvetica",
            fontSize=8,
            leading=10,
            alignment=TA_CENTER,
            textColor=INK,
        ),
        "h2": ParagraphStyle(
            "ManualH2",
            parent=sample["Heading1"],
            fontName="Helvetica-Bold",
            fontSize=18,
            leading=21,
            textColor=NAVY,
            spaceAfter=10,
            keepWithNext=True,
        ),
        "h3": ParagraphStyle(
            "ManualH3",
            parent=sample["Heading2"],
            fontName="Helvetica-Bold",
            fontSize=12,
            leading=14.5,
            textColor=NAVY,
            spaceBefore=8,
            spaceAfter=5,
            keepWithNext=True,
        ),
        "h4": ParagraphStyle(
            "ManualH4",
            parent=sample["Heading3"],
            fontName="Helvetica-Bold",
            fontSize=9.8,
            leading=12,
            textColor=BLUE,
            spaceBefore=6,
            spaceAfter=4,
            keepWithNext=True,
        ),
        "list": ParagraphStyle(
            "ManualList",
            parent=sample["BodyText"],
            fontName="Helvetica",
            fontSize=8.9,
            leading=12,
            textColor=INK,
            leftIndent=2,
            firstLineIndent=0,
        ),
        "code": ParagraphStyle(
            "ManualCode",
            parent=sample["Code"],
            fontName="Courier",
            fontSize=7.1,
            leading=9.2,
            textColor=INK,
            leftIndent=4,
            rightIndent=4,
            borderWidth=0.5,
            borderColor=LINE,
            borderPadding=7,
            backColor=LIGHT,
            spaceBefore=5,
            spaceAfter=8,
        ),
        "quote": ParagraphStyle(
            "ManualQuote",
            parent=sample["BodyText"],
            fontName="Helvetica",
            fontSize=8.6,
            leading=11.3,
            textColor=INK,
        ),
        "cover_title": ParagraphStyle(
            "CoverTitle",
            parent=sample["Title"],
            fontName="Helvetica-Bold",
            fontSize=28,
            leading=33,
            alignment=TA_CENTER,
            textColor=NAVY,
            spaceAfter=12,
        ),
        "cover_subtitle": ParagraphStyle(
            "CoverSubtitle",
            parent=sample["BodyText"],
            fontName="Helvetica",
            fontSize=12,
            leading=16,
            alignment=TA_CENTER,
            textColor=MUTED,
        ),
        "toc_title": ParagraphStyle(
            "TocTitle",
            parent=sample["Title"],
            fontName="Helvetica-Bold",
            fontSize=23,
            leading=27,
            textColor=NAVY,
            spaceAfter=10,
        ),
        "toc_level": ParagraphStyle(
            "TocLevel",
            parent=sample["BodyText"],
            fontName="Helvetica",
            fontSize=8.8,
            leading=12,
            leftIndent=0,
            firstLineIndent=0,
            textColor=INK,
            spaceBefore=2,
        ),
    }


STYLES = build_styles()


class ManualDocTemplate(BaseDocTemplate):
    def __init__(self, filename: str):
        super().__init__(
            filename,
            pagesize=A4,
            leftMargin=LEFT,
            rightMargin=RIGHT,
            topMargin=TOP,
            bottomMargin=BOTTOM,
            title="DigiSign PoC - Document Signing User Manual",
            author="ECUPK PoC team",
            subject="Operational guide for Bank iD SIGN and DigiSign Identify signing",
        )
        frame = Frame(
            LEFT,
            BOTTOM,
            PAGE_WIDTH - LEFT - RIGHT,
            PAGE_HEIGHT - TOP - BOTTOM,
            id="content",
        )
        self.addPageTemplates(
            [
                PageTemplate(id="Cover", frames=[frame], onPage=draw_cover_footer),
                PageTemplate(id="Content", frames=[frame], onPage=draw_content_frame),
            ]
        )
        self._heading_sequence = 0

    def beforeDocument(self):
        self._heading_sequence = 0

    def afterFlowable(self, flowable):
        if isinstance(flowable, Paragraph) and flowable.style.name == "ManualH2":
            self._heading_sequence += 1
            key = f"section-{self._heading_sequence}"
            title = flowable.getPlainText()
            self.canv.bookmarkPage(key)
            self.canv.addOutlineEntry(title, key, level=0, closed=False)
            self.notify("TOCEntry", (0, title, self.page, key))


def set_document_metadata(canvas):
    canvas.setTitle("DigiSign PoC - Document Signing User Manual")
    canvas.setAuthor("ECUPK PoC team")
    canvas.setSubject(
        "User guidance and technical flow for Bank iD SIGN and DigiSign Identify signing"
    )
    canvas.setKeywords(
        "DigiSign, Bank iD SIGN, DigiSign Identify, document signing, PoC, user manual"
    )


def draw_cover_footer(canvas, doc):
    set_document_metadata(canvas)
    canvas.saveState()
    canvas.setStrokeColor(LINE)
    canvas.setLineWidth(0.5)
    canvas.line(LEFT, 15 * mm, PAGE_WIDTH - RIGHT, 15 * mm)
    canvas.setFont("Helvetica", 7.5)
    canvas.setFillColor(MUTED)
    canvas.drawString(LEFT, 10 * mm, "DigiSign PoC - Document Signing User Manual")
    canvas.drawRightString(PAGE_WIDTH - RIGHT, 10 * mm, f"Page {doc.page}")
    canvas.restoreState()


def draw_content_frame(canvas, doc):
    set_document_metadata(canvas)
    canvas.saveState()
    canvas.setFont("Helvetica-Bold", 7.4)
    canvas.setFillColor(NAVY)
    canvas.drawString(LEFT, PAGE_HEIGHT - 12 * mm, "DIGISIGN POC")
    canvas.setFont("Helvetica", 7.4)
    canvas.setFillColor(MUTED)
    canvas.drawRightString(
        PAGE_WIDTH - RIGHT,
        PAGE_HEIGHT - 12 * mm,
        "Document Signing User Manual | Version 1.0",
    )
    canvas.setStrokeColor(LINE)
    canvas.setLineWidth(0.5)
    canvas.line(LEFT, PAGE_HEIGHT - 15 * mm, PAGE_WIDTH - RIGHT, PAGE_HEIGHT - 15 * mm)
    canvas.line(LEFT, 15 * mm, PAGE_WIDTH - RIGHT, 15 * mm)
    canvas.setFont("Helvetica", 7.4)
    canvas.setFillColor(MUTED)
    canvas.drawString(LEFT, 10 * mm, "Controlled PoC operating guide")
    canvas.drawRightString(PAGE_WIDTH - RIGHT, 10 * mm, f"Page {doc.page}")
    canvas.restoreState()


def cover_story():
    metadata = [
        ["Audience", "PoC operators, demonstrators, testers, and signing recipients"],
        ["Application", "DigiSign integration PoC"],
        ["Scope", "One PDF, one recipient, one signature field"],
        ["Signing methods", "Bank iD SIGN; DigiSign Identify + simple signature"],
        ["Version", "1.0 - July 2026"],
        ["Status", "Controlled PoC operating guide"],
    ]
    story = [
        Spacer(1, 28 * mm),
        Paragraph("DigiSign Document Signing PoC", STYLES["cover_title"]),
        Paragraph("User Manual", STYLES["cover_title"]),
        Spacer(1, 4 * mm),
        Paragraph(
            "End-to-end operator and recipient guidance for embedded document "
            "signing with Bank iD SIGN or DigiSign Identify.",
            STYLES["cover_subtitle"],
        ),
        Spacer(1, 14 * mm),
    ]

    stages = Table(
        [
            [
                Paragraph("<b>1</b><br/>Prepare", STYLES["stage"]),
                Paragraph("<b>2</b><br/>Create", STYLES["stage"]),
                Paragraph("<b>3</b><br/>Sign", STYLES["stage"]),
                Paragraph("<b>4</b><br/>Verify", STYLES["stage"]),
            ]
        ],
        colWidths=[(PAGE_WIDTH - LEFT - RIGHT) / 4] * 4,
        rowHeights=[18 * mm],
    )
    stages.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, -1), PALE_BLUE),
                ("BOX", (0, 0), (-1, -1), 0.8, BLUE),
                ("INNERGRID", (0, 0), (-1, -1), 0.5, colors.white),
                ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
                ("ALIGN", (0, 0), (-1, -1), "CENTER"),
            ]
        )
    )
    story.extend([stages, Spacer(1, 14 * mm)])

    table_data = [
        [
            Paragraph("Document field", STYLES["table_header"]),
            Paragraph("Value", STYLES["table_header"]),
        ]
    ] + [
        [Paragraph(inline_markup(left), STYLES["small"]), Paragraph(inline_markup(right), STYLES["small"])]
        for left, right in metadata
    ]
    control = Table(table_data, colWidths=[38 * mm, PAGE_WIDTH - LEFT - RIGHT - 38 * mm])
    control.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, 0), NAVY),
                ("TEXTCOLOR", (0, 0), (-1, 0), colors.white),
                ("GRID", (0, 0), (-1, -1), 0.45, LINE),
                ("BACKGROUND", (0, 1), (-1, -1), colors.white),
                ("VALIGN", (0, 0), (-1, -1), "TOP"),
                ("LEFTPADDING", (0, 0), (-1, -1), 6),
                ("RIGHTPADDING", (0, 0), (-1, -1), 6),
                ("TOPPADDING", (0, 0), (-1, -1), 5),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 5),
            ]
        )
    )
    story.extend([control, Spacer(1, 8 * mm)])
    story.append(
        callout(
            "<b>Important:</b> This manual is suitable for controlled operation and "
            "assessment of the PoC. The application itself is not production-ready. "
            "Production gaps are listed in section 17.",
            warning=True,
        )
    )
    story.extend([NextPageTemplate("Content"), PageBreak()])
    return story


def callout(value: str, warning: bool = False):
    border = WARNING_BORDER if warning else BLUE
    background = WARNING_BG if warning else VERY_PALE_BLUE
    table = Table([[Paragraph(value, STYLES["quote"])]], colWidths=[PAGE_WIDTH - LEFT - RIGHT])
    table.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, -1), background),
                ("BOX", (0, 0), (-1, -1), 0.8, border),
                ("LEFTPADDING", (0, 0), (-1, -1), 9),
                ("RIGHTPADDING", (0, 0), (-1, -1), 9),
                ("TOPPADDING", (0, 0), (-1, -1), 7),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 7),
            ]
        )
    )
    return table


def contents_story():
    toc = TableOfContents()
    toc.levelStyles = [STYLES["toc_level"]]
    return [
        Paragraph("Contents", STYLES["toc_title"]),
        Paragraph(
            "Use the numbered sections for the complete operating procedure. "
            "For a first live test, read sections 3, 5, 7, 8, 9, 10 or 11, 12, and 13.",
            STYLES["body"],
        ),
        Spacer(1, 3 * mm),
        toc,
        PageBreak(),
    ]


def parse_table(lines: list[str]):
    rows = []
    for line in lines:
        cells = [cell.strip() for cell in line.strip().strip("|").split("|")]
        if all(re.fullmatch(r":?-{3,}:?", cell) for cell in cells):
            continue
        rows.append(cells)
    if not rows:
        return Spacer(1, 0)

    count = len(rows[0])
    available = PAGE_WIDTH - LEFT - RIGHT
    if count == 2:
        widths = [available * 0.31, available * 0.69]
    elif count == 3:
        widths = [available * 0.22, available * 0.31, available * 0.47]
    elif count == 4 and rows[0][0].lower() == "signing route":
        widths = [available * 0.18, available * 0.26, available * 0.23, available * 0.33]
    elif count == 4:
        widths = [available * 0.08, available * 0.27, available * 0.45, available * 0.20]
    else:
        widths = [available / count] * count

    body_style = ParagraphStyle(
        "TableBody",
        parent=STYLES["small"],
        fontSize=7.2 if count >= 4 else 7.7,
        leading=9.4 if count >= 4 else 10.1,
        textColor=INK,
    )
    data = []
    for row_index, row in enumerate(rows):
        normalized = row + [""] * (count - len(row))
        data.append(
            [
                Paragraph(
                    inline_markup(cell),
                    STYLES["table_header"] if row_index == 0 else body_style,
                )
                for cell in normalized[:count]
            ]
        )

    table = Table(data, colWidths=widths, repeatRows=1, hAlign="LEFT")
    style_commands = [
        ("BACKGROUND", (0, 0), (-1, 0), NAVY),
        ("TEXTCOLOR", (0, 0), (-1, 0), colors.white),
        ("GRID", (0, 0), (-1, -1), 0.4, LINE),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 5),
        ("RIGHTPADDING", (0, 0), (-1, -1), 5),
        ("TOPPADDING", (0, 0), (-1, -1), 4),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 4),
    ]
    for row_index in range(1, len(data)):
        style_commands.append(
            (
                "BACKGROUND",
                (0, row_index),
                (-1, row_index),
                colors.white if row_index % 2 else LIGHT,
            )
        )
    table.setStyle(TableStyle(style_commands))
    return table


def make_list(items: list[str], ordered: bool):
    if not ordered:
        rows = [
            [
                Paragraph("-", STYLES["list"]),
                Paragraph(inline_markup(re.sub(r"^\[[ xX]\]\s*", "", item)), STYLES["list"]),
            ]
            for item in items
        ]
        table = Table(
            rows,
            colWidths=[6 * mm, PAGE_WIDTH - LEFT - RIGHT - 6 * mm],
            hAlign="LEFT",
        )
        table.setStyle(
            TableStyle(
                [
                    ("VALIGN", (0, 0), (-1, -1), "TOP"),
                    ("TEXTCOLOR", (0, 0), (0, -1), BLUE),
                    ("LEFTPADDING", (0, 0), (-1, -1), 0),
                    ("RIGHTPADDING", (0, 0), (-1, -1), 2),
                    ("TOPPADDING", (0, 0), (-1, -1), 1),
                    ("BOTTOMPADDING", (0, 0), (-1, -1), 2),
                ]
            )
        )
        return table

    flow_items = []
    for item in items:
        item = re.sub(r"^\[[ xX]\]\s*", "", item)
        flow_items.append(
            ListItem(
                Paragraph(inline_markup(item), STYLES["list"]),
                leftIndent=13,
            )
        )
    arguments = {
        "bulletType": "1",
        "leftIndent": 15,
        "bulletFontName": "Helvetica",
        "bulletFontSize": 8,
        "bulletColor": BLUE,
        "spaceBefore": 2,
        "spaceAfter": 4,
    }
    arguments["start"] = "1"
    return ListFlowable(flow_items, **arguments)


def markdown_body_story(source: str):
    lines = ascii_safe(source).splitlines()
    start = next(index for index, line in enumerate(lines) if line.startswith("## 1. Purpose"))
    lines = lines[start:]
    story = []
    index = 0
    first_section = True

    def collect_paragraph(at: int):
        parts = []
        while at < len(lines):
            line = lines[at]
            if not line.strip():
                break
            if re.match(r"^#{2,4}\s", line):
                break
            if line.startswith("```") or line.startswith(">") or line.startswith("|"):
                break
            if re.match(r"^\s*(?:[-*]|\d+\.)\s+", line):
                break
            parts.append(line.strip())
            at += 1
        return " ".join(parts), at

    while index < len(lines):
        line = lines[index]
        stripped = line.strip()
        if not stripped:
            index += 1
            continue

        if stripped.startswith("## "):
            title = stripped[3:]
            section_match = re.match(r"^(\d+)\.", title)
            compact_section = (
                section_match is not None
                and int(section_match.group(1)) in {6, 14, 18}
            )
            if not first_section and not compact_section:
                story.append(PageBreak())
            first_section = False
            if compact_section:
                story.append(Spacer(1, 5 * mm))
            story.append(Paragraph(inline_markup(title), STYLES["h2"]))
            index += 1
            continue

        if stripped.startswith("### "):
            story.append(Paragraph(inline_markup(stripped[4:]), STYLES["h3"]))
            index += 1
            continue

        if stripped.startswith("#### "):
            story.append(Paragraph(inline_markup(stripped[5:]), STYLES["h4"]))
            index += 1
            continue

        if stripped.startswith("```"):
            index += 1
            code = []
            while index < len(lines) and not lines[index].strip().startswith("```"):
                code.append(lines[index].rstrip())
                index += 1
            index += 1
            story.append(Preformatted("\n".join(code), STYLES["code"]))
            continue

        if stripped.startswith(">"):
            quote = []
            while index < len(lines) and lines[index].strip().startswith(">"):
                quote.append(lines[index].strip().lstrip(">").strip())
                index += 1
            story.append(callout(inline_markup(" ".join(quote)), warning=True))
            story.append(Spacer(1, 3 * mm))
            continue

        if stripped.startswith("|"):
            table_lines = []
            while index < len(lines) and lines[index].strip().startswith("|"):
                table_lines.append(lines[index])
                index += 1
            story.append(parse_table(table_lines))
            story.append(Spacer(1, 3 * mm))
            continue

        list_match = re.match(r"^\s*(-|\*|\d+\.)\s+(.*)", line)
        if list_match:
            ordered = list_match.group(1)[0].isdigit()
            items = []
            while index < len(lines):
                current = lines[index]
                match = re.match(r"^\s*(-|\*|\d+\.)\s+(.*)", current)
                if not match or match.group(1)[0].isdigit() != ordered:
                    break
                item_parts = [match.group(2).strip()]
                index += 1
                while index < len(lines):
                    continuation = lines[index]
                    if not continuation.strip():
                        index += 1
                        break
                    if re.match(r"^\s*(-|\*|\d+\.)\s+", continuation):
                        break
                    if re.match(r"^#{2,4}\s", continuation) or continuation.startswith("|"):
                        break
                    item_parts.append(continuation.strip())
                    index += 1
                items.append(" ".join(item_parts))
            story.append(make_list(items, ordered))
            continue

        paragraph, index = collect_paragraph(index)
        if paragraph:
            story.append(Paragraph(inline_markup(paragraph), STYLES["body"]))
        else:
            index += 1

    return story


def build_pdf():
    source = SOURCE.read_text(encoding="utf-8")
    TEMP_DIR.mkdir(parents=True, exist_ok=True)
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)

    story = []
    story.extend(cover_story())
    story.extend(contents_story())
    story.extend(markdown_body_story(source))

    doc = ManualDocTemplate(str(TEMP_PDF))
    doc.multiBuild(story)

    reader = PdfReader(str(TEMP_PDF))
    if len(reader.pages) < 20:
        raise RuntimeError(f"Expected at least 20 pages, generated {len(reader.pages)}.")

    required = [
        "Bank iD SIGN",
        "DigiSign Identify",
        "Create envelope and start signing",
        "Authoritative signing result",
        "PoC acceptance test",
    ]
    extracted = "\n".join(page.extract_text() or "" for page in reader.pages)
    missing = [value for value in required if value not in extracted]
    if missing:
        raise RuntimeError(f"Required manual content missing: {missing}")

    for page_number, page in enumerate(reader.pages, start=1):
        page_text = (page.extract_text() or "").strip()
        if len(page_text) < 60:
            raise RuntimeError(f"Page {page_number} appears unexpectedly empty.")

    os.replace(TEMP_PDF, OUTPUT)
    try:
        TEMP_DIR.rmdir()
        TEMP_DIR.parent.rmdir()
        TEMP_DIR.parent.parent.rmdir()
    except OSError:
        pass
    print(f"Generated {OUTPUT} ({len(reader.pages)} pages)")


if __name__ == "__main__":
    build_pdf()
