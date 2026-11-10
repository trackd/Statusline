using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace StatusLine;

internal sealed class PSReadLineRepaint {
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly ConcurrentDictionary<Type, PSReadLineRepaint?> Editors = new();

    private readonly Func<object?> getSingleton;
    private readonly Action<object> finishRepaint;
    private readonly Func<object, int>? initialRow;
    private object? singleton;

    private PSReadLineRepaint(Type type, FieldInfo field, FieldInfo waiting, MethodInfo render) {
        getSingleton = Expression.Lambda<Func<object?>>(
            Expression.Convert(Expression.Field(null, field), typeof(object))).Compile();
        ParameterExpression editor = Expression.Parameter(typeof(object));
        UnaryExpression instance = Expression.Convert(editor, type);
        finishRepaint = Expression.Lambda<Action<object>>(
            Expression.IfThen(Expression.Field(instance, waiting), Expression.Call(instance, render, Expression.Constant(true))),
            editor).Compile();
        if (type.GetField("_initialY", Instance) is { } row && row.FieldType == typeof(int)) {
            initialRow = Expression.Lambda<Func<object, int>>(Expression.Field(instance, row), editor).Compile();
        }
        Repaint = TryRepaint;
    }

    internal static PSReadLineRepaint? Get(Type type) => Editors.GetOrAdd(type, static editorType => {
        try {
            FieldInfo? field = editorType.GetField("_singleton", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo? waiting = editorType.GetField("_waitingToRender", Instance);
            MethodInfo? render = editorType.GetMethod("Render", Instance, [typeof(bool)]);
            return field?.FieldType == editorType && waiting?.FieldType == typeof(bool) && render?.ReturnType == typeof(void)
                ? new PSReadLineRepaint(editorType, field, waiting, render)
                : null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) {
            return null;
        }
    });

    internal Func<bool> Repaint { get; }

    internal int? GetInitialRow() {
        if (initialRow is null) {
            return null;
        }
        try {
            return getSingleton() is { } editor ? initialRow(editor) : null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) {
            return null;
        }
    }

    private bool TryRepaint() {
        object? editor = Volatile.Read(ref singleton);
        for (int attempt = 0; attempt < 2; attempt++) {
            try {
                editor ??= getSingleton();
                if (editor is not null) {
                    Volatile.Write(ref singleton, editor);
                    finishRepaint(editor);
                    return true;
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException) {
                Interlocked.CompareExchange(ref singleton, null, editor);
                editor = null;
            }
        }
        return false;
    }
}
