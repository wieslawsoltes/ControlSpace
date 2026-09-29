using ControlSpace.Core;
using ControlSpace.Engineering;

int count = 0;
void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
void Test(string name, Action action) { action(); count++; Console.WriteLine("PASS " + name); }
ProjectNavigator Navigator() { var n = new ProjectNavigator(); n.SetProject(DemoProject.Create()); return n; }
ControlProject Large()
{
    var p = DemoProject.Create(); p.Blocks.Clear(); p.Screens.Clear();
    for (int i = 0; i < 1000; i++)
    {
        p.Blocks.Add(new("b" + i, "Program_" + i.ToString("D4"), i + 1, BlockLanguage.SCL, false, [], ""));
        p.Screens.Add(new("s" + i, "Screen_" + i.ToString("D4"), 800, 480, []));
    }
    return p;
}
Test("empty navigation is safe", () => { var n = new ProjectNavigator(); Check(n.Move(ProjectNavigationMove.Next) is null && n.RevealActive() is null); n.ExpandAll(false); Check(n.VisibleEntries.Count == 0); });
Test("root and declared blocks preserve model order", () => { var n = Navigator(); Check(n.VisibleEntries[0].Id == "project"); Check(n.Entries.Where(e => e.Target?.StartsWith("block:") == true).Select(e => e.Id).SequenceEqual(new[] { "block:main", "block:speed" })); });
Test("same snapshot does not rebuild index or projection", () => { var n = new ProjectNavigator(); var p = DemoProject.Create(); n.SetProject(p); long b = n.IndexBuilds, q = n.ProjectionBuilds; Check(!n.SetProject(p) && n.IndexBuilds == b && n.ProjectionBuilds == q); });
foreach (string kind in new[] { "tag-comment", "tag-value", "source", "network", "hmi-object", "device-position" }) Test("outline retained for " + kind, () =>
{
    var n = new ProjectNavigator(); var p = DemoProject.Create(); n.SetProject(p); var next = ProjectSnapshot.Clone(p);
    switch (kind)
    {
        case "tag-comment": next.Tags[0] = next.Tags[0] with { Comment = "new comment" }; break;
        case "tag-value": next.Tags[0] = next.Tags[0] with { InitialValue = 1 }; break;
        case "source": next.Blocks[1] = next.Blocks[1] with { Source = "// retained outline" }; break;
        case "network": next.Blocks[0].Networks[0] = next.Blocks[0].Networks[0] with { Title = "new network title" }; break;
        case "hmi-object": next.Screens[0].Objects[0] = next.Screens[0].Objects[0] with { Text = "new label" }; break;
        case "device-position": next.Devices[0] = next.Devices[0] with { X = 45 }; break;
    }
    Check(!n.SetProject(next) && n.IndexBuilds == 1 && n.ProjectionBuilds == 1);
});
foreach (string kind in new[] { "name", "tag-count", "block-name", "block-number", "language", "cyclic", "order", "screen-name", "screen-size", "device-name" }) Test("outline refreshes for " + kind, () =>
{
    var n = new ProjectNavigator(); var p = DemoProject.Create(); n.SetProject(p); var next = ProjectSnapshot.Clone(p);
    switch (kind)
    {
        case "name": next = next with { Name = "Other" }; break;
        case "tag-count": next.Tags.RemoveAt(0); break;
        case "block-name": next.Blocks[0] = next.Blocks[0] with { Name = "Renamed" }; break;
        case "block-number": next.Blocks[0] = next.Blocks[0] with { Number = 7 }; break;
        case "language": next.Blocks[0] = next.Blocks[0] with { Language = BlockLanguage.SCL }; break;
        case "cyclic": next.Blocks[0] = next.Blocks[0] with { Cyclic = false }; break;
        case "order": next.Blocks.Reverse(); break;
        case "screen-name": next.Screens[0] = next.Screens[0] with { Name = "Other screen" }; break;
        case "screen-size": next.Screens[0] = next.Screens[0] with { Width = 900 }; break;
        case "device-name": next.Devices[0] = next.Devices[0] with { Name = "Other device" }; break;
    }
    Check(n.SetProject(next) && n.IndexBuilds == 2);
});
Test("collapse hides descendants, not following siblings", () => { var n = Navigator(); n.SetExpanded("blocks", false); Check(n.VisibleIndexOf("block:main") < 0 && n.VisibleIndexOf("tag-folder") >= 0); });
Test("search shows matching ancestors in collapsed folders", () => { var n = Navigator(); n.SetExpanded("plc", false); n.SetFilter("sPeEd"); Check(n.VisibleEntries.Select(e => e.Id).SequenceEqual(new[] { "project", "plc", "blocks", "block:speed" })); });
Test("search clearing restores original expansion", () => { var n = Navigator(); n.SetExpanded("blocks", false); n.SetFilter("Main"); n.SetFilter(""); Check(n.VisibleIndexOf("block:main") < 0); });
Test("missing search is an empty projection", () => { var n = Navigator(); n.SetFilter("not present 123"); Check(n.VisibleEntries.Count == 0 && n.Move(ProjectNavigationMove.Last) is null); });
Test("same normalized filter does not rebuild", () => { var n = Navigator(); n.SetFilter(" Main "); long q = n.ProjectionBuilds; Check(!n.SetFilter("Main") && n.ProjectionBuilds == q); });
Test("child key expands then selects first child", () => { var n = Navigator(); n.Select("blocks"); n.SetExpanded("blocks", false); Check(n.Move(ProjectNavigationMove.Child) == "blocks" && n.IsExpanded("blocks")); Check(n.Move(ProjectNavigationMove.Child) == "add-block"); });
Test("parent key selects parent then collapses it", () => { var n = Navigator(); n.Select("block:main"); Check(n.Move(ProjectNavigationMove.Parent) == "blocks"); Check(n.Move(ProjectNavigationMove.Parent) == "blocks" && !n.IsExpanded("blocks")); });
Test("movement clamps at first and last visible node", () => { var n = Navigator(); n.Move(ProjectNavigationMove.First); Check(n.Move(ProjectNavigationMove.Previous) == "project"); n.Move(ProjectNavigationMove.Last); Check(n.Move(ProjectNavigationMove.Next) == "library"); });
Test("active document is not a second independent selection", () => { var n = Navigator(); n.Select("tags"); n.SetActive("block:main"); Check(n.SelectedId == "tags" && n.ActiveTarget == "block:main"); });
Test("explicit reveal expands ancestors and selects active target", () => { var n = Navigator(); n.ExpandAll(false); n.SetActive("block:speed"); Check(n.RevealActive() == "block:speed" && n.VisibleIndexOf("block:speed") >= 0); });
Test("model reveal never clears an incompatible filter", () => { var n = Navigator(); n.SetFilter("Main"); n.SetActive("block:speed"); Check(n.RevealActive() is null && n.Filter == "Main"); });
Test("selection and collapse survive unrelated snapshots", () => { var n = new ProjectNavigator(); var p = DemoProject.Create(); n.SetProject(p); n.Select("tags"); n.SetExpanded("blocks", false); var q = ProjectSnapshot.Clone(p); q.Tags[0] = q.Tags[0] with { Comment = "x" }; n.SetProject(q); Check(n.SelectedId == "tags" && !n.IsExpanded("blocks")); });
Test("new project resets expansion and selection", () => { var n = Navigator(); n.Select("tags"); n.ExpandAll(false); n.SetProject(DemoProject.Create() with { Id = "new" }); Check(n.SelectedId is null && n.IsExpanded("blocks")); });
Test("deleted selected node is removed cleanly", () => { var n = new ProjectNavigator(); var p = DemoProject.Create(); n.SetProject(p); n.Select("block:main"); var q = ProjectSnapshot.Clone(p); q.Blocks.RemoveAt(0); n.SetProject(q); Check(n.SelectedId is null && n.Find("block:main") is null); });
Test("description includes metadata and parent path", () => { var n = Navigator(); n.Select("block:main"); string text = n.DescribeSelection(); Check(text.Contains("LAD") && text.Contains("Program blocks") && text.Contains("Cyclic")); });
Test("full supported outline uses linear projection work", () => { var n = new ProjectNavigator(); n.SetProject(Large()); n.SetFilter("Program_"); Check(n.VisibleEntries.Count == 1003 && n.LastProjectionVisits <= n.Entries.Count * 2); Check(n.VisibleIndexOf("block:b999") == 1002); });
Test("large search matches simple ancestry oracle", () =>
{
    var n = new ProjectNavigator(); n.SetProject(Large());
    foreach (string filter in new[] { "0", "999", "Program", "Screen", "PLC", "zzzz" })
    {
        n.SetFilter(filter); var expected = new HashSet<string>();
        for (int i = 0; i < n.Entries.Count; i++) if (n.Entries[i].Caption.Contains(filter, StringComparison.OrdinalIgnoreCase))
        {
            expected.Add(n.Entries[i].Id); int depth = n.Entries[i].Depth;
            for (int j = i - 1; j >= 0 && depth > 0; j--) if (n.Entries[j].Depth < depth) { expected.Add(n.Entries[j].Id); depth = n.Entries[j].Depth; }
        }
        Check(n.VisibleEntries.Select(e => e.Id).SequenceEqual(n.Entries.Where(e => expected.Contains(e.Id)).Select(e => e.Id)));
    }
});
Test("navigation does not mutate project or undo history", () => { var w = new Workspace(DemoProject.Create()); var p = w.Project; var n = new ProjectNavigator(); n.SetProject(p); n.SetFilter("Main"); n.SetExpanded("blocks", false); n.Select("block:main"); Check(ReferenceEquals(w.Project, p) && !w.IsDirty && !w.CanUndo); });
Console.WriteLine($"Navigation regression groups: {count} passed, 0 failed.");
