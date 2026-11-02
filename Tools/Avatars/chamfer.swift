import Foundation
import CoreGraphics
import ImageIO
import UniformTypeIdentifiers

// Cuts the corners off a square avatar at 45 degrees, in the same proportion the game's buttons use.
//
// Startbutton2.png cuts 118 of its 500 pixels. A face is not a button, and that much off a 256-pixel
// avatar ate into it, so the avatars take half of that share -- 59 of 500, which is 30 pixels. The
// corners become transparent, nothing else is touched, and running it twice changes nothing, since
// what it would cut is already gone.
//
// Cut from the squares in Tools/Avatars/source, never from an avatar that has been cut already: a
// smaller cut cannot put back a corner a larger one removed.
//
//   swift Tools/Avatars/chamfer.swift Assets/Art/Avatars/Robo1.png [more...]

let cutFraction: CGFloat = 59.0 / 500.0

func octagon(_ side: CGFloat, _ cut: CGFloat) -> CGPath {
    let path = CGMutablePath()
    path.move(to: CGPoint(x: cut, y: 0))
    path.addLine(to: CGPoint(x: side - cut, y: 0))
    path.addLine(to: CGPoint(x: side, y: cut))
    path.addLine(to: CGPoint(x: side, y: side - cut))
    path.addLine(to: CGPoint(x: side - cut, y: side))
    path.addLine(to: CGPoint(x: cut, y: side))
    path.addLine(to: CGPoint(x: 0, y: side - cut))
    path.addLine(to: CGPoint(x: 0, y: cut))
    path.closeSubpath()
    return path
}

for path in CommandLine.arguments.dropFirst() {
    let url = URL(fileURLWithPath: path)

    guard let source = CGImageSourceCreateWithURL(url as CFURL, nil),
          let image = CGImageSourceCreateImageAtIndex(source, 0, nil) else {
        print("skipped \(path): not an image")
        continue
    }

    let width = image.width, height = image.height
    let side = CGFloat(min(width, height))
    let cut = (side * cutFraction).rounded()

    guard let ctx = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8,
                              bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(),
                              bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else {
        print("skipped \(path): could not draw it")
        continue
    }

    ctx.setAllowsAntialiasing(true)
    ctx.interpolationQuality = .high
    ctx.addPath(octagon(side, cut))
    ctx.clip()
    ctx.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))

    guard let cut_image = ctx.makeImage(),
          let out = CGImageDestinationCreateWithURL(url as CFURL, UTType.png.identifier as CFString, 1, nil) else {
        print("skipped \(path): could not write it back")
        continue
    }

    CGImageDestinationAddImage(out, cut_image, nil)
    CGImageDestinationFinalize(out)
    print("cut \(Int(cut)) px off each corner of \(url.lastPathComponent)")
}
