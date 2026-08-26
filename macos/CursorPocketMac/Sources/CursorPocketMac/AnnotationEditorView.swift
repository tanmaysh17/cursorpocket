import SwiftUI
import CursorPocketMacKit

struct AnnotationEditorView: View {
    @ObservedObject var model: AnnotationEditorModel
    @FocusState private var textFieldFocused: Bool

    var body: some View {
        VStack(spacing: 0) {
            toolbar
            GeometryReader { geometry in
                canvasArea(in: geometry.size)
            }
            .background(Color.black.opacity(0.85))
            statusBar
        }
        .frame(minWidth: 720, minHeight: 480)
    }

    // MARK: Toolbar

    private var toolbar: some View {
        HStack(spacing: 8) {
            ForEach(AnnotationTool.allCases, id: \.self) { tool in
                toolButton(tool)
            }
            Button {
                model.armCrop()
            } label: {
                Text("Crop (C)")
                    .font(.system(size: 11, weight: model.cropArmed ? .bold : .regular))
                    .padding(.horizontal, 8)
                    .frame(height: 24)
                    .background(
                        model.cropArmed ? Theme.ready.opacity(0.35) : Color.primary.opacity(0.06),
                        in: RoundedRectangle(cornerRadius: 5))
            }
            .buttonStyle(.plain)

            Divider().frame(height: 20)

            ForEach(Array(AnnotationPalette.markColors.enumerated()), id: \.offset) { index, hex in
                colorDot(index: index, hex: hex)
            }

            Divider().frame(height: 20)

            Button("Undo") { model.undo() }.disabled(!model.history.canUndo)
            Button("Redo") { model.redo() }.disabled(!model.history.canRedo)

            Spacer()

            Button("Cancel (Esc)") { _ = model.onClose?() }
            Button {
                model.saveHandler?(model)
            } label: {
                Text(model.saveTarget == .newCapture ? "Save crop as new (⏎)" : "Save (⏎)")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(Theme.pine)
                    .padding(.horizontal, 12)
                    .frame(height: 26)
                    .background(Theme.ready, in: RoundedRectangle(cornerRadius: 6))
            }
            .buttonStyle(.plain)
        }
        .padding(.horizontal, 12)
        .frame(height: 44)
    }

    private func toolButton(_ tool: AnnotationTool) -> some View {
        let isArmed = model.tool == tool && !model.cropArmed
        return Button {
            model.arm(tool)
        } label: {
            Text("\(model.label(for: tool)) (\(String(tool.acceleratorKey).uppercased()))")
                .font(.system(size: 11, weight: isArmed ? .bold : .regular))
                .padding(.horizontal, 8)
                .frame(height: 24)
                .background(
                    isArmed ? Theme.ready.opacity(0.35) : Color.primary.opacity(0.06),
                    in: RoundedRectangle(cornerRadius: 5))
        }
        .buttonStyle(.plain)
    }

    private func colorDot(index: Int, hex: String) -> some View {
        let components = AnnotationPalette.components(of: hex)
        return Button {
            model.colorIndex = index
        } label: {
            Circle()
                .fill(Color(red: components.red, green: components.green, blue: components.blue))
                .frame(width: 16, height: 16)
                .overlay(Circle().stroke(
                    model.colorIndex == index ? Color.primary : Color.primary.opacity(0.2),
                    lineWidth: model.colorIndex == index ? 2 : 1))
        }
        .buttonStyle(.plain)
    }

    private var statusBar: some View {
        HStack {
            Text(model.status)
                .font(.system(size: 11, design: .monospaced))
                .foregroundStyle(.secondary)
            Spacer()
            if model.cropRect != nil {
                Text("Crop saves as a NEW capture — the original stays")
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundStyle(Theme.ready)
            }
        }
        .padding(.horizontal, 12)
        .frame(height: 28)
    }

    // MARK: Canvas

    private func canvasArea(in size: CGSize) -> some View {
        let transform = fitTransform(in: size)
        return ZStack(alignment: .topLeading) {
            Image(nsImage: model.image)
                .resizable()
                .interpolation(.high)
                .frame(width: transform.displaySize.width, height: transform.displaySize.height)
                .offset(x: transform.offset.x, y: transform.offset.y)

            Canvas { context, _ in
                for mark in model.marks {
                    draw(mark, in: &context, transform: transform, isSelected: mark.id == model.selectedMarkID)
                }
                if let pending = model.pendingMark {
                    draw(pending, in: &context, transform: transform, isSelected: false)
                }
                if let crop = model.pendingCrop ?? model.cropRect {
                    let viewRect = transform.toView(crop)
                    context.stroke(
                        Path(viewRect), with: .color(Theme.ready),
                        style: StrokeStyle(lineWidth: 2, dash: [6, 4]))
                }
            }
            .allowsHitTesting(false)

            Color.clear
                .contentShape(Rectangle())
                .gesture(dragGesture(transform: transform))

            // Above the gesture layer, or the field could never be clicked.
            textEditorOverlay(transform: transform)
        }
        .frame(width: size.width, height: size.height)
        .clipped()
    }

    private func textEditorOverlay(transform: FitTransform) -> some View {
        Group {
            if let id = model.editingTextMarkID,
               let index = model.marks.firstIndex(where: { $0.id == id }) {
                let origin = transform.toView(model.marks[index].start)
                TextField("Type, then press Return", text: Binding(
                    get: { model.marks[index].text },
                    set: { model.marks[index].text = $0 }))
                    .textFieldStyle(.roundedBorder)
                    .focused($textFieldFocused)
                    .frame(width: 240)
                    .offset(x: origin.x, y: origin.y)
                    .onSubmit { model.finishTextEditing() }
                    .onAppear { textFieldFocused = true }
            }
        }
    }

    private func dragGesture(transform: FitTransform) -> some Gesture {
        DragGesture(minimumDistance: 0)
            .onChanged { value in
                guard model.editingTextMarkID == nil else { return }
                let start = transform.toImage(value.startLocation)
                let current = transform.toImage(value.location)
                if model.cropArmed {
                    model.pendingCrop = RegionSelection.clamp(
                        RegionSelection.rect(from: start, to: current),
                        to: CGRect(origin: .zero, size: model.imagePixelSize))
                    return
                }
                switch model.tool {
                case .select:
                    if model.pendingMark == nil, model.selectedMarkID == nil {
                        model.selectedMarkID = model.marks.last(where: { $0.hitTest(start) })?.id
                    }
                case .text, .marker:
                    break
                case .freehand:
                    if var pending = model.pendingMark {
                        pending.path.append(current)
                        pending.end = current
                        model.pendingMark = pending
                    } else {
                        model.pendingMark = AnnotationMark(
                            tool: .freehand, start: start, end: current, path: [start, current],
                            colorIndex: model.colorIndex)
                    }
                default:
                    model.pendingMark = AnnotationMark(
                        tool: model.tool, start: start, end: current,
                        colorIndex: model.colorIndex,
                        strokeWidth: model.tool.defaultStrokeWidth)
                }
            }
            .onEnded { value in
                let start = transform.toImage(value.startLocation)
                let end = transform.toImage(value.location)
                // A click on the canvas while a text block is being edited
                // commits (or discards) that block instead of acting.
                if model.editingTextMarkID != nil {
                    model.finishTextEditing()
                    return
                }
                if model.cropArmed {
                    let rect = RegionSelection.clamp(
                        RegionSelection.rect(from: start, to: end),
                        to: CGRect(origin: .zero, size: model.imagePixelSize))
                    model.pendingCrop = nil
                    model.cropArmed = false
                    if RegionSelection.isUsable(rect) {
                        model.cropRect = rect
                        model.status = "Crop set — Enter saves it as a new capture"
                    }
                    return
                }
                switch model.tool {
                case .select:
                    model.selectedMarkID = model.marks.last(where: { $0.hitTest(end) })?.id
                case .text:
                    model.commit(AnnotationMark(
                        tool: .text, start: end, end: end, text: "",
                        colorIndex: model.colorIndex))
                case .marker:
                    // A click stamps the next step number at the point; the
                    // number itself is derived from array order, never stored.
                    model.commit(AnnotationMark(
                        tool: .marker, start: end, end: end,
                        colorIndex: model.colorIndex))
                default:
                    if let pending = model.pendingMark {
                        model.pendingMark = nil
                        let bounds = pending.bounds
                        if bounds.width >= 3 || bounds.height >= 3 {
                            model.commit(pending)
                        }
                    }
                }
            }
    }

    private func draw(_ mark: AnnotationMark, in context: inout GraphicsContext, transform: FitTransform, isSelected: Bool) {
        let componentValues = AnnotationPalette.components(of: AnnotationPalette.color(at: mark.colorIndex))
        let color = Color(red: componentValues.red, green: componentValues.green, blue: componentValues.blue)
        let width = max(1, mark.strokeWidth * transform.scale)

        switch mark.tool {
        case .select:
            break
        case .box:
            context.stroke(Path(transform.toView(mark.bounds)), with: .color(color), lineWidth: width)
        case .ellipse:
            context.stroke(
                Path(ellipseIn: transform.toView(mark.bounds)), with: .color(color), lineWidth: width)
        case .line:
            var path = Path()
            path.move(to: transform.toView(mark.start))
            path.addLine(to: transform.toView(mark.end))
            context.stroke(path, with: .color(color), lineWidth: width)
        case .arrow:
            var path = Path()
            path.move(to: transform.toView(mark.start))
            path.addLine(to: transform.toView(mark.end))
            let head = AnnotationGeometry.arrowHead(
                from: mark.start, to: mark.end, length: 6 * mark.strokeWidth)
            path.move(to: transform.toView(head.left))
            path.addLine(to: transform.toView(mark.end))
            path.addLine(to: transform.toView(head.right))
            context.stroke(path, with: .color(color), lineWidth: width)
        case .freehand:
            guard mark.path.count > 1 else { break }
            var path = Path()
            path.move(to: transform.toView(mark.path[0]))
            for point in mark.path.dropFirst() { path.addLine(to: transform.toView(point)) }
            context.stroke(path, with: .color(color), lineWidth: width)
        case .highlight:
            // Same multiply + opacity as the Kit renderer, so the live canvas
            // matches the flattened file. State is set on a copy so the blend
            // never leaks into the next mark.
            var path = Path()
            path.move(to: transform.toView(mark.start))
            path.addLine(to: transform.toView(mark.end))
            var highlightContext = context
            highlightContext.opacity = AnnotationHighlightStyle.opacity
            highlightContext.blendMode = .multiply
            highlightContext.stroke(
                path, with: .color(color),
                style: StrokeStyle(lineWidth: width, lineCap: .round))
        case .marker:
            let center = transform.toView(mark.start)
            let radius = MarkerNumbering.radius(
                forImageShortEdge: min(model.imagePixelSize.width, model.imagePixelSize.height))
                * transform.scale
            context.fill(
                Path(ellipseIn: CGRect(
                    x: center.x - radius, y: center.y - radius,
                    width: radius * 2, height: radius * 2)),
                with: .color(color))
            if let number = MarkerNumbering.number(of: mark, in: model.marks) {
                context.draw(
                    Text("\(number)")
                        .font(.system(size: radius * 1.1, weight: .bold))
                        .foregroundColor(.white),
                    at: center, anchor: .center)
            }
        case .redact:
            context.fill(Path(transform.toView(mark.bounds)), with: .color(.black))
        case .text:
            let origin = transform.toView(mark.start)
            let fontSize = max(14, mark.strokeWidth * 8) * transform.scale
            context.draw(
                Text(mark.text.isEmpty ? "…" : mark.text)
                    .font(.system(size: fontSize, weight: .bold))
                    .foregroundColor(color),
                at: CGPoint(x: origin.x, y: origin.y + fontSize / 2),
                anchor: .leading)
        }

        if isSelected {
            context.stroke(
                Path(transform.toView(mark.bounds).insetBy(dx: -4, dy: -4)),
                with: .color(Theme.ready),
                style: StrokeStyle(lineWidth: 1.5, dash: [4, 3]))
        }
    }

    // MARK: Fit transform

    private struct FitTransform {
        let scale: CGFloat
        let offset: CGPoint
        let displaySize: CGSize

        func toView(_ point: CGPoint) -> CGPoint {
            CGPoint(x: point.x * scale + offset.x, y: point.y * scale + offset.y)
        }

        func toView(_ rect: CGRect) -> CGRect {
            CGRect(
                x: rect.origin.x * scale + offset.x,
                y: rect.origin.y * scale + offset.y,
                width: rect.width * scale,
                height: rect.height * scale)
        }

        func toImage(_ point: CGPoint) -> CGPoint {
            CGPoint(x: (point.x - offset.x) / scale, y: (point.y - offset.y) / scale)
        }
    }

    private func fitTransform(in size: CGSize) -> FitTransform {
        let pixels = model.imagePixelSize
        guard pixels.width > 0, pixels.height > 0, size.width > 0, size.height > 0 else {
            return FitTransform(scale: 1, offset: .zero, displaySize: pixels)
        }
        let scale = min(size.width / pixels.width, size.height / pixels.height, 1)
        let display = CGSize(width: pixels.width * scale, height: pixels.height * scale)
        let offset = CGPoint(
            x: (size.width - display.width) / 2,
            y: (size.height - display.height) / 2)
        return FitTransform(scale: scale, offset: offset, displaySize: display)
    }
}
