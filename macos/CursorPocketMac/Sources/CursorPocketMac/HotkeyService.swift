import AppKit
import Carbon.HIToolbox
import CursorPocketMacKit

/// Global hotkeys through Carbon `RegisterEventHotKey` — the supported API for
/// system-wide chords that needs no Accessibility grant. Registrations are
/// dynamic so recording-scoped keys (bare Escape) exist only while recording.
final class HotkeyService {
    private var eventHandler: EventHandlerRef?
    private var registrations: [UInt32: (ref: EventHotKeyRef?, handler: () -> Void)] = [:]
    private var nextIdentifier: UInt32 = 1
    private static let signature: OSType = 0x4350_4B54 // 'CPKT'

    init() {
        var eventType = EventTypeSpec(
            eventClass: OSType(kEventClassKeyboard),
            eventKind: UInt32(kEventHotKeyPressed))
        let callback: EventHandlerUPP = { _, event, userData in
            guard let event, let userData else { return noErr }
            var hotKeyID = EventHotKeyID()
            let status = GetEventParameter(
                event,
                EventParamName(kEventParamDirectObject),
                EventParamType(typeEventHotKeyID),
                nil,
                MemoryLayout<EventHotKeyID>.size,
                nil,
                &hotKeyID)
            guard status == noErr, hotKeyID.signature == HotkeyService.signature else { return noErr }
            let service = Unmanaged<HotkeyService>.fromOpaque(userData).takeUnretainedValue()
            service.fire(identifier: hotKeyID.id)
            return noErr
        }
        InstallEventHandler(
            GetEventDispatcherTarget(),
            callback,
            1,
            &eventType,
            UnsafeMutableRawPointer(Unmanaged.passUnretained(self).toOpaque()),
            &eventHandler)
    }

    deinit {
        unregisterAll()
        if let eventHandler { RemoveEventHandler(eventHandler) }
    }

    @discardableResult
    func register(_ spec: HotkeySpec, handler: @escaping () -> Void) -> UInt32 {
        let identifier = nextIdentifier
        nextIdentifier += 1
        var reference: EventHotKeyRef?
        let status = RegisterEventHotKey(
            spec.keyCode,
            spec.modifiers.carbonFlags,
            EventHotKeyID(signature: Self.signature, id: identifier),
            GetEventDispatcherTarget(),
            0,
            &reference)
        guard status == noErr else { return 0 }
        registrations[identifier] = (reference, handler)
        return identifier
    }

    func unregister(_ identifier: UInt32) {
        guard let registration = registrations.removeValue(forKey: identifier) else { return }
        if let reference = registration.ref { UnregisterEventHotKey(reference) }
    }

    func unregisterAll() {
        for identifier in Array(registrations.keys) { unregister(identifier) }
    }

    private func fire(identifier: UInt32) {
        guard let handler = registrations[identifier]?.handler else { return }
        DispatchQueue.main.async(execute: handler)
    }
}
