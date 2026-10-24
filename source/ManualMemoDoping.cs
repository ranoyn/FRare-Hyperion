using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace FreeNormalFinder {
public sealed partial class ToolForm {
    readonly DopingMemoEditor manualDopingEditor = new DopingMemoEditor("manualDoping", false);
    readonly TextBox manualDopingSummary = new TextBox {
        Name = "manualDopingSummary", Dock = DockStyle.Fill, Multiline = true,
        ReadOnly = true, ScrollBars = ScrollBars.Vertical
    };
    readonly Button manualDopingChange = new Button {
        Name = "manualDopingChange", Text = "変更", AutoSize = true
    };
    string manualDopingSelectedId;
    bool fillingManualDoping;

    void BuildManualDopingFields(TableLayoutPanel layout) {
        var fields = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty
        };
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        fields.Controls.Add(manualDopingEditor, 0, 0);
        fields.Controls.Add(manualDopingSummary, 0, 1);
        layout.Controls.Add(fields, 0, 3);
        manualDopingEditor.AddAction(manualDopingChange);
        manualDopingEditor.Changed += delegate {
            if (!fillingManualDoping) ShowManualDopingCalculation();
        };
        manualDopingChange.Click += delegate {
            if (manualDopingSelectedId == null) return;
            try {
                var person = FindManualPerson(manualDopingSelectedId);
                UpdateManualAbility(person.Id, person.Antenna, person.Head,
                    person.Color1, person.Color2, person.Pattern, person.Body,
                    person.Name, ManualDopingDraft(person));
            } catch (Exception error) { ShowError(error); }
        };
    }

    int?[] ManualDopingDraft(ManualMemoIndividual person) {
        var values = (int?[])person.Doping.Clone();
        // Editing one stat must not turn the other unset fields into explicit zeroes.
        if (person.Id == manualDopingSelectedId)
            for (int i = 0; i < values.Length; i++)
                if (manualDopingEditor.IsStatEdited(i))
                    values[i] = (int)manualDopingEditor.Inputs[i].Value;
        return values;
    }

    void RefreshManualDopingFields() {
        ManualMemoIndividual person;
        fillingManualDoping = true;
        try {
            if (manualMemoDiagram.SelectedRecord == null ||
                !manualMemoRecords.TryGetValue(manualMemoDiagram.SelectedRecord, out person)) {
                manualDopingSelectedId = null;
                manualDopingEditor.SetUnavailable();
                manualDopingChange.Enabled = false;
                manualDopingSummary.Clear();
                return;
            }
            manualDopingSelectedId = person.Id;
            manualDopingEditor.Enabled = manualDopingChange.Enabled = true;
            manualDopingEditor.LoadNotes(new[] {
                new DopingEntry {
                    Generation = person.Generation,
                    Counts = person.Doping.Select(n => n ?? 0).ToArray()
                }
            }, person.Generation, person.Generation);
        } finally { fillingManualDoping = false; }
        ShowManualDopingCalculation();
    }

    void ShowManualDopingCalculation() {
        if (manualDopingSelectedId == null) return;
        var person = FindManualPerson(manualDopingSelectedId);
        var result = CalculateManualDopingCore(person.Id, ManualDopingDraft(person));
        string[] labels = { "HP", "AP", "こうげきりょく", "ぼうぎょりょく", "すばやさ" };
        manualDopingSummary.Text = "ドーピング合計値：" + String.Join("　",
            Enumerable.Range(0, 5).Select(i => labels[i] + " +" + result.Total[i])) +
            "\r\n現世代：" + String.Join(" / ", result.Current) +
            "　継承：" + String.Join(" / ", result.Inherited);
    }

    public DopingCalculation CalculateManualDoping(string personId) {
        return CalculateManualDopingCore(personId, null);
    }

    DopingCalculation CalculateManualDopingCore(string personId, int?[] draft) {
        FindManualPerson(personId);
        // Calculate from stored relationships, not the currently visible diagram rows.
        var branches = manualMemoBranches.ToDictionary(b => b.Id);
        var people = manualMemoPeople.ToDictionary(p => p.Id);
        var cache = new Dictionary<string, DopingCalculation>();
        var path = new HashSet<string>();
        Func<string, DopingCalculation> visit = null;
        visit = id => {
            ManualMemoBranch branch;
            var result = new DopingCalculation();
            if (id == null || !branches.TryGetValue(id, out branch) || path.Contains(id)) {
                result.Complete = false;
                return result;
            }
            DopingCalculation cached;
            if (cache.TryGetValue(id, out cached)) return cached;
            path.Add(id);
            ManualMemoIndividual person;
            if (branch.PersonId != null && people.TryGetValue(branch.PersonId, out person)) {
                var counts = person.Id == personId && draft != null ? draft : person.Doping;
                for (int i = 0; i < 5; i++) result.Current[i] = (counts[i] ?? 0) * 2;
            }
            if (branch.ParentA != null) {
                var a = visit(branch.ParentA);
                ManualMemoBranch right;
                bool reincarnation = branch.ParentB != null &&
                    branches.TryGetValue(branch.ParentB, out right) && right.Hidden;
                if (reincarnation) {
                    result.Complete = a.Complete;
                    for (int i = 0; i < 5; i++)
                        result.Inherited[i] = DopingNotes.ReincarnationInheritance(a, i);
                } else {
                    var b = visit(branch.ParentB);
                    result.Complete = a.Complete && b.Complete;
                    for (int i = 0; i < 5; i++)
                        result.Inherited[i] = DopingNotes.BirthInheritance(a, b, i);
                }
            }
            path.Remove(id);
            cache[id] = result;
            return result;
        };
        return visit(manualMemoBranches.Single(b => b.PersonId == personId).Id);
    }
}
}
