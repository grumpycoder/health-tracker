import SwiftUI

/// Today's notes: jot how you feel or what's going on; the Coach check-in reads them.
struct NotesSection: View {
    @EnvironmentObject private var store: Store
    @State private var draft = ""
    @State private var editing: NoteEntry?
    @State private var editText = ""
    @FocusState private var focused: Bool

    var body: some View {
        Section {
            ForEach(store.notes(on: .today).reversed()) { n in
                Button { editText = n.text; editing = n } label: {
                    HStack(alignment: .firstTextBaseline) {
                        Text(n.time.formatted(date: .omitted, time: .shortened)).font(.caption).foregroundStyle(.secondary).monospacedDigit()
                        Text(n.text)
                    }
                }
                .buttonStyle(.plain)
                .swipeActions { Button("Delete", role: .destructive) { store.deleteNote(n.id) } }
            }
            HStack(alignment: .bottom) {
                TextField("How are you feeling right now?", text: $draft, axis: .vertical)
                    .lineLimit(1...5).focused($focused)
                Button("Add") { store.addNote(draft); draft = ""; focused = false }
                    .buttonStyle(.bordered)
                    .disabled(draft.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
            }
        } header: { Text("Notes") } footer: {
            Text("Today's notes are shared with the Coach check-in for context — keep anything private out of them.")
        }
        .alert("Edit note", isPresented: Binding(get: { editing != nil }, set: { if !$0 { editing = nil } })) {
            TextField("Note", text: $editText)
            Button("Save") { if let n = editing { store.updateNote(n.id, text: editText) } }
            Button("Cancel", role: .cancel) {}
        }
    }
}
