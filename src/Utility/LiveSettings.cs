using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace StatusLine.Configuration;

/// <summary>PowerShell property view over the existing validated, atomic configuration API.</summary>
internal static class LiveSettings {
    internal static PSObject Create(Type surface, Type configuration, params string[] readOnly) {
        Guid owner = Runspace.DefaultRunspace.InstanceId;
        string access = $"[{surface.FullName}]::Configuration";
        string guard = $"if ([runspace]::DefaultRunspace.InstanceId -ne [guid]'{owner}') {{ throw 'Settings belong to another runspace.' }}";
        var view = new PSObject();
        view.TypeNames.Insert(0, surface.FullName + "Settings");
        foreach (System.Reflection.PropertyInfo property in configuration.GetProperties()) {
            string name = property.Name;
            var getter = ScriptBlock.Create($"if ([runspace]::DefaultRunspace.InstanceId -ne [guid]'{owner}') {{ return }}; $settings = {access}; if ($null -ne $settings) {{ ,$settings.{name} }}");
            ScriptBlock? setter = null;
            if (!readOnly.Contains(name, StringComparer.Ordinal)) {
                setter = ScriptBlock.Create($"param($value) {guard}; [{surface.FullName}]::Configure(@{{ {name} = $value }})");
            }

            view.Properties.Add(new PSScriptProperty(name, getter, setter));
        }

        return view;
    }
}
