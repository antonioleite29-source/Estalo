import Foundation
import CoreGraphics
import ImageIO
import UniformTypeIdentifiers

// Draws the button the game already uses, at a size meant to be nine-sliced rather than stretched.
//
// Measured off Assets/Art/Backgrounds/Startbutton2.png (2399 x 500): a flat slab of #7379A1 with the
// corners cut at 118 of its 500 pixels, and a band along the top edge -- 35 pixels of #3A3D51 at half
// alpha -- which is what gives the slab its lip. Nothing else: no gradient, no outline.
//
// The white one carries the band as mid-grey at half alpha, which is the same thing in ratio: tint it
// #7379A1 with the Image's own Color and the band comes out at #3A3D51, exactly as in the original.

let size = 128              // square, so one set of borders works on all four sides
let chamfer: CGFloat = 35   // what 118 of the original 500 comes to on a 150-tall button
let band: CGFloat = 10      // what 35 of the original 500 comes to on a 150-tall button
let border = 44             // written into the .meta: wider than the chamfer, under half of 100

struct Variant {
    let name: String
    let body: (CGFloat, CGFloat, CGFloat, CGFloat)
    let band: (CGFloat, CGFloat, CGFloat, CGFloat)
}

let half: CGFloat = 128.0 / 255.0

let variants = [
    // To be tinted by the Image's Color. White body, mid-grey band, both at the original's ratio.
    Variant(name: "ButtonNineSlice", body: (1, 1, 1, 1), band: (half, half, half, half)),
    // The slate of the Jogar button, baked.
    Variant(name: "ButtonSlateNineSlice",
            body: (0x73/255, 0x79/255, 0xA1/255, 1),
            band: (0x3A/255, 0x3D/255, 0x51/255, half)),
    // The cyan of a chosen mode -- 1x1, 2x2 -- baked, darkened the same way for the band.
    Variant(name: "ButtonCyanNineSlice",
            body: (34/255, 184/255, 207/255, 1),
            band: (17/255, 92/255, 104/255, half)),
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

    let full = CGRect(x: 0, y: 0, width: CGFloat(size), height: CGFloat(size))
    let shape = octagon(full, chamfer)

    // The slab, with the top band left out of it: the band is not drawn over the body, it replaces
    // it, so it stays half-transparent against whatever is behind the button.
    ctx.saveGState()
    ctx.addPath(shape)
    ctx.clip()

    ctx.setFillColor(red: variant.body.0, green: variant.body.1, blue: variant.body.2, alpha: variant.body.3)
    ctx.fill(CGRect(x: 0, y: 0, width: CGFloat(size), height: CGFloat(size) - band))

    ctx.setFillColor(red: variant.band.0, green: variant.band.1, blue: variant.band.2, alpha: variant.band.3)
    ctx.fill(CGRect(x: 0, y: CGFloat(size) - band, width: CGFloat(size), height: band))

    ctx.restoreGState()

    let image = ctx.makeImage()!
    let path = CommandLine.arguments[1] + "/" + variant.name + ".png"
    let out = CGImageDestinationCreateWithURL(URL(fileURLWithPath: path) as CFURL,
                                              UTType.png.identifier as CFString, 1, nil)!
    CGImageDestinationAddImage(out, image, nil)
    CGImageDestinationFinalize(out)
    print("wrote \(path)  \(size)x\(size), border \(border)")
}
