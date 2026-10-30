using System.Text;
using StatuslineWorld.wit.Imports.terminalExtensions.statusline.v0_1_0;
using static StatuslineWorld.wit.Imports.terminalExtensions.statusline.v0_1_0.ITypesImports;

namespace StatuslineWorld;

// Same lifecycle as Contour's agent-status example, but forwards an entire rendered frame.
public sealed class StatuslineWorldExportsImpl : IStatuslineWorldExports {
    private static string variable = "statusline_frame";
    private static string shownText = string.Empty;
    private static byte[] frame = [];

    public static EventKinds Load(List<(string, string)> config) {
        foreach ((string key, string value) in config) {
            if (key == "variable") {
                variable = value;
            }
            else if (key == "text") {
                shownText = value;
            }
        }
        foreach ((string name, string value) in IHostImports.State().userVars) {
            if (name == variable) {
                shownText = value;
                break;
            }
        }
        frame = Encoding.UTF8.GetBytes(shownText);
        return EventKinds.USER_VAR;
    }

    public static RenderRequest Update(Event ev) {
        if (ev.Tag == Event.Tags.UserVar) {
            (string name, string value) = ev.AsUserVar;
            if (name == variable) {
                if (!string.Equals(shownText, value, StringComparison.Ordinal)) {
                    shownText = value;
                    frame = Encoding.UTF8.GetBytes(value);
                    return RenderRequest.RENDER;
                }
            }
        }
        return ev.Tag == Event.Tags.Resized ? RenderRequest.RENDER : RenderRequest.NONE;
    }

    public static byte[] Render(uint cols, uint rows) => frame;
}
