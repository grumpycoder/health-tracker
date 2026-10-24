import SwiftUI

/// A number field for the number/decimal pad, which has no return key. While it's being edited the
/// keyboard bar offers Cancel (puts the value it had before back) and Done. Edits apply as you
/// type, so tapping another control mid-edit keeps them; blank = nil.
struct PadNumberField: View {
    let placeholder: String
    @Binding var value: Double?
    var decimals = true
    @FocusState private var focused: Bool
    @State private var text = ""
    @State private var original: Double?

    init(_ placeholder: String, value: Binding<Double?>, decimals: Bool = true) {
        self.placeholder = placeholder; _value = value; self.decimals = decimals
    }

    /// Whole-number fields (reps, seconds, mg).
    init(_ placeholder: String, value: Binding<Int?>) {
        self.init(placeholder, value: Binding(get: { value.wrappedValue.map(Double.init) },
                                              set: { value.wrappedValue = $0.map { Int($0.rounded()) } }), decimals: false)
    }

    var body: some View {
        TextField(placeholder, text: Binding(get: { focused ? text : format(value) },
                                             set: { text = $0; value = parse($0) }))
            .keyboardType(decimals ? .decimalPad : .numberPad)
            .focused($focused)
            .onChange(of: focused) { _, now in if now { original = value; text = format(value) } }
            .toolbar {
                if focused {
                    ToolbarItemGroup(placement: .keyboard) {
                        Button("Cancel") { value = original; focused = false }
                        Spacer()
                        Button("Done") { focused = false }.fontWeight(.semibold)
                    }
                }
            }
    }

    private func format(_ v: Double?) -> String {
        v.map { $0.formatted(.number.precision(.fractionLength(decimals ? 0...1 : 0...0)).grouping(.never)) } ?? ""
    }

    private func parse(_ s: String) -> Double? {
        let t = s.trimmingCharacters(in: .whitespaces).replacingOccurrences(of: Locale.current.decimalSeparator ?? ".", with: ".")
        return t.isEmpty ? nil : Double(t)
    }
}
