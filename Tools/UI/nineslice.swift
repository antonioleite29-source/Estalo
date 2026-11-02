import Foundation
import CoreGraphics
import ImageIO
import UniformTypeIdentifiers

// Draws the button sprite the game already uses -- a chamfered slab with a lighter top and a darker
// lip along the bottom -- at a size meant to be nine-sliced rather than stretched.

let size = 160            // square, so the same borders work on every side
let chamfer: CGFloat = 34 // the corner cut, well inside the border
let topBand: CGFloat = 14
let bottomLip: CGFloat = 22

struct Variant { let name: String; let r: CGFloat; let g: CGFloat; let b: CGFloat }

let variants = [
    // White, for an Image that carries its own colour: tinting this one gives any button.
    Variant(name: "ButtonNineSlice", r: 1, g: 1, b: 1),
    // The slate the Jogar button is painted in today.
    Variant(name: "ButtonSlateNineSlice", r: 0x73/255, g: 0x79/255, b: 0xA1/255),
    // The cyan a chosen mode -- 1x1 or 2x2 -- is painted in.
    Variant(name: "ButtonCyanNineSlice", r: 34/255, g: 184/255, b: 207/255),
]

func octagon(_ rect: CGRect, _ cut: CGFloat) -> CGPath {
    let path = CGMutablePath()
    let x0 = rect.minX, x1 = rect.maxX, y0 = rect.minY, y1 = rect.maxY
    path.move(to: CGPoint(x: x0 + cut, y: y0))
    path.addLine(to: CGPoint(x: x1 - cut, y: y0))
    path.addLine(to: CGPoint(x: x1, y: y0 + cut))
    path.addLine(to: CGPoint(x: x1, y: y1 - cut))
    path.addLine(to: CGPoint(x: x1 - cut, y: y1))
    path.addLine(to: CGPoint(x: x0 + cut, y: y1))
    path.addLine(to: CGPoint(x: x0, y: y1 - cut))
    path.addLine(to: CGPoint(x: x0, y: y0 + cut))
    path.closeSubpath()
    return path
}

for variant in variants {
    let ctx = CGContext(data: nil, width: size, height: size, bitsPerComponent: 8, bytesPerRow: 0,
                        space: CGColorSpaceCreateDeviceRGB(),
                        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    ctx.setAllowsAntialiasing(true)
    ctx.interpolationQuality = .high

    let full = CGRect(x: 0.5, y: 0.5, width: CGFloat(size) - 1, height: CGFloat(size) - 1)
    let shape = octagon(full, chamfer)

    // The body, a little under full strength so the top band reads as a highlight on top of it.
    ctx.addPath(shape)
    ctx.setFillColor(red: variant.r * 0.90, green: variant.g * 0.90, blue: variant.b * 0.90, alpha: 1)
    ctx.fillPath()

    ctx.saveGState()
    ctx.addPath(shape)
    ctx.clip()

    // Lighter along the top, darker along the bottom: the slab reads as a solid with a lip under it,
    // and nine-slicing keeps both bands their own height at any button size.
    ctx.setFillColor(red: variant.r, green: variant.g, blue: variant.b, alpha: 1)
    ctx.fill(CGRect(x: 0, y: CGFloat(size) - topBand, width: CGFloat(size), height: topBand))

    ctx.setFillColor(red: variant.r * 0.62, green: variant.g * 0.62, blue: variant.b * 0.62, alpha: 1)
    ctx.fill(CGRect(x: 0, y: 0, width: CGFloat(size), height: bottomLip))

    ctx.restoreGState()

    // A thin dark edge all the way round, so the shape still reads on a pale background.
    ctx.addPath(shape)
    ctx.setStrokeColor(red: 0, green: 0, blue: 0, alpha: 0.22)
    ctx.setLineWidth(3)
    ctx.strokePath()

    let image = ctx.makeImage()!
    let path = CommandLine.arguments[1] + "/" + variant.name + ".png"
    let out = CGImageDestinationCreateWithURL(URL(fileURLWithPath: path) as CFURL, UTType.png.identifier as CFString, 1, nil)!
    CGImageDestinationAddImage(out, image, nil)
    CGImageDestinationFinalize(out)
    print("wrote \(path)")
}
