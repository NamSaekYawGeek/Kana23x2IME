/// Preedit remains inside the keyboard until the user explicitly commits it.
public struct CompositionBuffer {
    public private(set) var text: String

    public init(text: String = "") { self.text = text }
    public mutating func append(_ value: String) { text += value }
    public mutating func clear() { text = "" }
    public mutating func transform(_ operation: (String) -> String) { text = operation(text) }

    public mutating func backspace() {
        if !text.isEmpty { text.removeLast() }
    }
}
