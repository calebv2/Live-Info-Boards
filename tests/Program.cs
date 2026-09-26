using System;
using System.Collections.Generic;

internal static class Program
{
    private static int Main()
    {
        try
        {
            Dictionary<string, string> entries;
            string error;

            Assert(InfoBoardDocument.TryParse("{\"townnews\":\"Hello\\nTown\"}", out entries, out error), "A valid channel document must parse.");
            Assert(entries.Count == 1 && entries["townnews"] == "Hello\nTown", "Escaped JSON newlines must become board line breaks.");
            Assert(!InfoBoardDocument.TryParse("{\"../bad\":\"x\"}", out entries, out error), "Path-like channel names must be rejected.");
            Assert(!InfoBoardDocument.TryParse("{\"townnews\":123}", out entries, out error), "A board value must be text.");
            Assert(!InfoBoardDocument.TryParse("{\"this-channel-name-is-too-long\":\"x\"}", out entries, out error), "Channel names longer than 20 characters must be rejected.");
            Assert(!InfoBoardDocument.TryParse("{\"townnews\":\"" + new string('x', 501) + "\"}", out entries, out error), "Board text longer than 500 characters must be rejected.");
            Assert(!InfoBoardDocument.TryParse("[]", out entries, out error), "The document root must be an object.");

            var authority = new InfoBoardAuthority();
            IReadOnlyList<InfoBoardChange> changes;
            Assert(authority.TryReadChanges("{\"townnews\":\"One\"}", out changes, out error), "The first valid document must be accepted.");
            Assert(changes.Count == 1 && changes[0].Channel == "townnews" && changes[0].Text == "One", "The first document must produce its board update.");
            authority.MarkApplied(changes[0]);
            Assert(authority.TryReadChanges("{\"townnews\":\"One\"}", out changes, out error), "An unchanged valid document must be accepted.");
            Assert(changes.Count == 0, "An unchanged document must not produce redundant board updates.");
            Assert(authority.TryReadChanges("{\"townnews\":\"Two\",\"events\":\"Soon\"}", out changes, out error), "An updated valid document must be accepted.");
            Assert(changes.Count == 2, "Changed and new channels must be emitted.");
            foreach (InfoBoardChange change in changes) authority.MarkApplied(change);
            Assert(!authority.TryReadChanges("{\"../bad\":\"x\"}", out changes, out error), "An invalid revision must be rejected.");
            Assert(authority.TryReadChanges("{\"townnews\":\"Two\",\"events\":\"Soon\"}", out changes, out error), "The last valid document must still be usable after an invalid revision.");
            Assert(changes.Count == 0, "An invalid revision must not reset successfully applied values.");

            Assert(InfoBoardDiagnosticFormatter.Format(54619, -688.3f, 129.2f, 74.9f, "custom1") ==
                "Infoboard entity=54619 position=(-688.3, 129.2, 74.9) channel='custom1'.", "Board diagnostics must expose the physical board and its channel.");

            var placementTracker = new InfoBoardPlacementTracker(3f);
            string settledKey;
            Assert(placementTracker.TryGetSettledKey(99966, "-741.2,135.1,23.8", 0f, out settledKey) && settledKey == "-741.2,135.1,23.8", "A newly observed board must be registered immediately.");
            Assert(placementTracker.TryGetSettledKey(99966, "-741.2,133.6,23.8", 2f, out settledKey) && settledKey == "-741.2,135.1,23.8", "Moving a registered board must retain its last stable key while restarting its settle timer.");
            Assert(placementTracker.TryGetSettledKey(99966, "-741.2,133.6,23.8", 4.9f, out settledKey) && settledKey == "-741.2,135.1,23.8", "A board must retain its last stable key until the new position settles.");
            Assert(placementTracker.TryGetSettledKey(99966, "-741.2,133.6,23.8", 5f, out settledKey) && settledKey == "-741.2,133.6,23.8", "A stationary board must register at its final position.");
            Assert(placementTracker.TryGetSettledKey(99966, "-742.7,133.5,25.8", 5.5f, out settledKey) && settledKey == "-741.2,133.6,23.8", "A later move must retain the last stable key until it settles.");

            Dictionary<string, InfoBoardDefinition> boards;
            Assert(InfoBoardRegistryDocument.TryParse("{\"boards\":{\"-688.3,129.2,74.9\":{\"channel\":\"welcome\",\"text\":\"Hello\"}}}", out boards, out error), "A position-keyed board registry must parse: " + error);
            Assert(boards["-688.3,129.2,74.9"].Channel == "welcome" && boards["-688.3,129.2,74.9"].Text == "Hello", "A board registry entry must retain its channel and text.");
            Assert(InfoBoardRegistryDocument.TryParse("{\"boards\":{\"-688.3,129.2,74.9\":{\"channel\":\"welcome\",\"text\":\"One\",\"rotation\":{\"enabled\":true,\"interval\":2,\"unit\":\"minutes\",\"messages\":[\"One\",\"Two\"]}}}}", out boards, out error), "A rotating board registry entry must parse: " + error);
            Assert(boards["-688.3,129.2,74.9"].Rotation.Messages.Count == 2 && boards["-688.3,129.2,74.9"].Rotation.Interval == 2, "A rotating board must retain its schedule and messages.");
            Dictionary<string, InfoBoardDefinition> emptyBoards;
            Assert(InfoBoardRegistryDocument.TryParse("{\"boards\":{\"empty\":{\"channel\":\"welcome\",\"text\":\"One\",\"rotation\":{\"enabled\":true,\"interval\":2,\"unit\":\"minutes\",\"messages\":[]}}}}", out emptyBoards, out error), "An enabled schedule may be saved before messages are added: " + error);
            Assert(boards["-688.3,129.2,74.9"].Rotation.TextAt(0f, "Fallback") == "One" && boards["-688.3,129.2,74.9"].Rotation.TextAt(120f, "Fallback") == "Two" && boards["-688.3,129.2,74.9"].Rotation.TextAt(240f, "Fallback") == "One", "A rotating board must advance on its configured minute interval and wrap around.");
            string largeMessage = new string('x', 2000);
            Assert(InfoBoardRegistryDocument.Serialize(boards).Contains("\"-688.3,129.2,74.9\""), "A board registry must serialize position keys.");
            Dictionary<string, InfoBoardDefinition> largeBoards;
            Assert(InfoBoardRegistryDocument.TryParse("{\"boards\":{\"large\":{\"channel\":\"welcome\",\"text\":\"" + largeMessage + "\",\"rotation\":{\"enabled\":true,\"interval\":1,\"unit\":\"minutes\",\"messages\":[\"" + largeMessage + "\",\"Next\"]}}}}", out largeBoards, out error), "Large rotating messages must parse: " + error);

            var registryAuthority = new InfoBoardRegistryAuthority();
            IReadOnlyList<InfoBoardRegistryChange> registryChanges;
            Assert(registryAuthority.GetChanges(boards, out registryChanges), "A registry definition must be considered for writing even with no loaded board.");
            Assert(registryChanges.Count == 1 && registryChanges[0].Channel == "welcome", "The registry must emit its channel for native-file creation.");
            registryAuthority.MarkApplied(registryChanges[0]);
            Assert(registryAuthority.GetChanges(boards, out registryChanges) && registryChanges.Count == 0, "An unchanged registry must not repeatedly write its native file.");

            var unconfirmedAuthority = new InfoBoardRegistryAuthority();
            Assert(unconfirmedAuthority.GetChanges(boards, out registryChanges) && registryChanges.Count == 1, "A new write must be requested.");
            Assert(unconfirmedAuthority.GetChanges(boards, out registryChanges) && registryChanges.Count == 1, "An unconfirmed native write must remain eligible for retry.");

            boards.Add("-700.0,130.0,75.0", new InfoBoardDefinition("other", "Other"));
            bool channelUnused;
            Assert(InfoBoardRegistryCleanup.Remove(boards, "-688.3,129.2,74.9", "welcome", out channelUnused), "A deleted board must be removed from the registry.");
            Assert(channelUnused && !boards.ContainsKey("-688.3,129.2,74.9"), "An unshared channel must be marked for native-file cleanup.");
            boards.Add("-701.0,130.0,75.0", new InfoBoardDefinition("other", "Shared"));
            Assert(InfoBoardRegistryCleanup.Remove(boards, "-700.0,130.0,75.0", "other", out channelUnused), "A second deleted board must be removable.");
            Assert(!channelUnused, "A channel still used by another board must not have its file deleted.");

            Console.WriteLine("PASS: infoboard document validation and change tracking.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL: " + exception.Message);
            return 1;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
