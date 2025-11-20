using System.Collections.ObjectModel;
using Microsoft.Maui.Controls.Shapes;
using Donezo.Services;
using System.Linq; // LINQ
using Microsoft.Maui; // Colors
using Microsoft.Maui.Storage; // Preferences
using System.Diagnostics;

namespace Donezo.Pages;

public partial class DashboardPage
{
    // Remove UI elements for list management; retain collections for role checks
    private readonly ObservableCollection<ListRecord> _listsObservable = new();
    private readonly ObservableCollection<SharedListRecord> _sharedListsObservable = new();
    private IReadOnlyList<ListRecord> _ownedLists = Array.Empty<ListRecord>();
    private IReadOnlyList<SharedListRecord> _sharedLists = Array.Empty<SharedListRecord>();
    private Label _completedBadge = new() { Text = "Completed", BackgroundColor = Colors.Green, TextColor = Colors.White, Padding = new Thickness(8, 2), IsVisible = false, FontAttributes = FontAttributes.Bold };

    private static bool _traceEnabled = true; // toggle for instrumentation
    private void Trace(string msg)
    { if (!_traceEnabled) return; try { Debug.WriteLine($"[DashTrace] {DateTime.UtcNow:HH:mm:ss.fff} {msg}"); } catch { } }

    // Simplified refresh (owned/shared for role logic and picker population moved) handled now by RefreshListsPickerAsync in main partial.

    private void UpdateCompletedBadge()
    { if (_allItems.Count == 0) { _completedBadge.IsVisible = false; return; } _completedBadge.IsVisible = _allItems.All(i => i.IsCompleted); }

    private void UpdateAllListSelectionVisuals() { /* picker handles visuals now */ }
}
