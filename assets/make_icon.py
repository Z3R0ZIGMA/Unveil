"""Draw the Unveil icon: a grey, faceted battery with a blue lightning bolt.

Same flat faceted style as the Unbound icon (same greys and outline), with the
Unveil accent blue for the bolt.

Needs PySide6. Unbound's virtual environment already has it:
  "..\\Unbound\\.venv\\Scripts\\python" assets\\make_icon.py
Writes assets/unveil.ico (16-256 px) and assets/unveil.png (256 px).
"""

import os
import struct

from PySide6.QtCore import QBuffer, QByteArray, QIODevice, QPointF, Qt
from PySide6.QtGui import QColor, QGuiApplication, QImage, QPainter, QPolygonF

HERE = os.path.dirname(os.path.abspath(__file__))
SIZES = [16, 24, 32, 48, 64, 128, 256]

# Unbound's facet greys and outline
TOP = "#B4B9C1"    # lightest, faces the light
LEFT = "#868C96"
RIGHT = "#5C626C"  # darkest
EDGE = "#3E434B"

# Unveil accent (plan.md section 5.1): light and dark facets of the bolt
BOLT_LIGHT = "#3b82f6"
BOLT_DARK = "#2563eb"


def P(x, y):
    return QPointF(x, y)


def render(size):
    img = QImage(size, size, QImage.Format_ARGB32)
    img.fill(Qt.transparent)
    p = QPainter(img)
    p.setRenderHint(QPainter.Antialiasing)
    p.scale(size / 256, size / 256)
    p.setPen(Qt.NoPen)

    # Battery body: an upright rectangle with chamfered corners, split down the
    # middle into a lit left facet and a shaded right facet, with a lit top bevel.
    l, r, t, b, ch = 58, 198, 46, 238, 18
    cx = (l + r) / 2
    body = [P(l + ch, t), P(r - ch, t), P(r, t + ch), P(r, b - ch), P(r - ch, b), P(l + ch, b), P(l, b - ch), P(l, t + ch)]

    left_facet = [P(l + ch, t), P(cx, t), P(cx, b), P(l + ch, b), P(l, b - ch), P(l, t + ch)]
    right_facet = [P(cx, t), P(r - ch, t), P(r, t + ch), P(r, b - ch), P(r - ch, b), P(cx, b)]
    top_bevel = [P(l + ch, t), P(r - ch, t), P(r, t + ch), P(cx, t + 40), P(l, t + ch)]

    for pts, colour in ((left_facet, LEFT), (right_facet, RIGHT), (top_bevel, TOP)):
        p.setBrush(QColor(colour))
        p.drawPolygon(QPolygonF(pts))

    # Terminal cap
    cap = [P(100, 18), P(156, 18), P(164, 46), P(92, 46)]
    p.setBrush(QColor(TOP))
    p.drawPolygon(QPolygonF(cap))

    # Lightning bolt, two facets
    p.setBrush(QColor(BOLT_LIGHT))
    p.drawPolygon(QPolygonF([P(146, 82), P(96, 152), P(128, 152), P(110, 206), P(160, 130), P(128, 130)]))
    p.setBrush(QColor(BOLT_DARK))
    p.drawPolygon(QPolygonF([P(128, 152), P(110, 206), P(160, 130), P(140, 130)]))

    # Outline at every size keeps small icons crisp on light taskbars
    pen = p.pen()
    pen.setStyle(Qt.SolidLine)
    pen.setColor(QColor(EDGE))
    pen.setWidthF(max(1.0, 256 / size * 0.9))
    pen.setJoinStyle(Qt.MiterJoin)
    p.setPen(pen)
    p.setBrush(Qt.NoBrush)
    p.drawPolygon(QPolygonF(body))
    p.drawPolygon(QPolygonF(cap))
    p.end()
    return img


def png_bytes(img):
    data = QByteArray()
    buf = QBuffer(data)
    buf.open(QIODevice.WriteOnly)
    img.save(buf, "PNG")
    return bytes(data)


def write_ico(path, images):
    """ICO file with PNG-compressed entries (supported since Windows Vista)."""
    pngs = [png_bytes(i) for i in images]
    out = struct.pack("<HHH", 0, 1, len(pngs))
    offset = 6 + 16 * len(pngs)
    for img, data in zip(images, pngs):
        w = img.width() if img.width() < 256 else 0  # 0 means 256
        out += struct.pack("<BBBBHHII", w, w, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    with open(path, "wb") as fh:
        fh.write(out + b"".join(pngs))


if __name__ == "__main__":
    app = QGuiApplication([])
    images = [render(s) for s in SIZES]
    write_ico(os.path.join(HERE, "unveil.ico"), images)
    images[-1].save(os.path.join(HERE, "unveil.png"))
    print("Wrote unveil.ico and unveil.png")
