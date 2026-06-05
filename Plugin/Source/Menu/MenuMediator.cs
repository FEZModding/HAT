using System.Collections;
using System.Reflection;
using FezEngine.Components;
using FezGame;
using Microsoft.Xna.Framework.Graphics;

namespace HatModLoader.Source.Menu;

public static class MenuMediator
{
    private static Type _menuBaseType;
    private static Type _mainMenuType;
    private static Type _menuLevelType;
    private static Type _menuItemType;
    private static FieldInfo _menuBaseCurrentMenuLevelField;
    private static FieldInfo _menuLevelParentField;
    private static FieldInfo _mainMenuRealMenuRootField;
    private static FieldInfo _menuBaseMenuRootField;
    private static MethodInfo _menuBaseChangeMenuLevelMethod;
    private static MethodInfo _menuBaseRenderToTextureMethod;
    private static PropertyInfo _menuLevelTitleProperty;
    private static PropertyInfo _menuLevelAButtonStringProperty;
    private static PropertyInfo _menuLevelBButtonStringProperty;
    private static FieldInfo _menuLevelIsDynamicField;
    private static FieldInfo _menuLevelOversizedField;
    private static FieldInfo _menuLevelOnResetField;
    private static FieldInfo _menuLevelOnPostDrawField;
    private static PropertyInfo _menuItemSelectableProperty;
    private static PropertyInfo _menuItemSuffixTextProperty;
    private static MethodInfo _menuLevelAddItemMethod;
    private static MethodInfo _menuLevelAddScrollableItemMethod;
    private static FieldInfo _menuLevelItemsField;
    
    public static event Action<object> MenuInitialized;
    
    static MenuMediator()
    {
        EnsureReflectionFieldsResolved();
        MenuInitialized += ModListMenuBuilder.InjectIntoMenuBase;
    }
    
    private static void EnsureReflectionFieldsResolved()
    {
        if (_menuBaseType != null)
        {
            return;
        }
        var fezAssembly = Assembly.GetAssembly(typeof(Fez));
        _menuBaseType = fezAssembly.GetType("FezGame.Components.MenuBase");
        _mainMenuType = fezAssembly.GetType("FezGame.Components.MainMenu");
        _menuLevelType = fezAssembly.GetType("FezGame.Structure.MenuLevel");
        _menuItemType = fezAssembly.GetType("FezGame.Structure.MenuItem");
        
        _menuBaseCurrentMenuLevelField = _menuBaseType.GetField("CurrentMenuLevel", BindingFlags.NonPublic | BindingFlags.Instance);
        _menuLevelParentField = _menuLevelType.GetField("Parent");
        _mainMenuRealMenuRootField = _mainMenuType.GetField("RealMenuRoot", BindingFlags.NonPublic | BindingFlags.Instance);
        _menuBaseMenuRootField = _menuBaseType.GetField("MenuRoot", BindingFlags.NonPublic | BindingFlags.Instance);
        _menuBaseChangeMenuLevelMethod = _menuBaseType.GetMethod("ChangeMenuLevel", new[] { _menuLevelType, typeof(bool) });
        _menuBaseRenderToTextureMethod = _menuBaseType.GetMethod("RenderToTexture", BindingFlags.NonPublic | BindingFlags.Instance);
        _menuLevelTitleProperty = _menuLevelType.GetProperty("Title");
        _menuLevelAButtonStringProperty = _menuLevelType.GetProperty("AButtonString");
        _menuLevelBButtonStringProperty = _menuLevelType.GetProperty("BButtonString");
        _menuLevelIsDynamicField = _menuLevelType.GetField("IsDynamic");
        _menuLevelOversizedField = _menuLevelType.GetField("Oversized");
        _menuLevelOnResetField = _menuLevelType.GetField("OnReset");
        _menuLevelOnPostDrawField = _menuLevelType.GetField("OnPostDraw");
        _menuItemSelectableProperty = _menuItemType.GetProperty("Selectable");
        _menuItemSuffixTextProperty = _menuItemType.GetProperty("SuffixText");
        _menuLevelAddItemMethod = _menuLevelType
            .GetMethod("AddItem", new[] { typeof(string), typeof(Action), typeof(bool), typeof(int) });
        _menuLevelAddScrollableItemMethod = _menuLevelType.GetMethods() 
            .First(m => m.Name == "AddItem" && m.GetParameters().Length == 6)
            .MakeGenericMethod(typeof(int));
        _menuLevelItemsField = _menuLevelType.GetField("Items");
    }
    
    internal static void OnMenuInitialized(object menuBase)
    {
        _menuLevelType.GetField("IsDynamic").SetValue(GetMenuRoot(menuBase), true);
        MenuInitialized?.Invoke(menuBase);
        
        // need to refresh the menu before the transition to it happens (pause menu)
        _menuBaseRenderToTextureMethod.Invoke(menuBase, null);
    }
    
    public static void OpenMenuLevel(object menuBase, LevelTemplate template)
    {
        var level = BuildMenuLevelObject(template);
        var currentLevel = _menuBaseCurrentMenuLevelField.GetValue(menuBase);
        _menuLevelParentField.SetValue(level, currentLevel);
        _menuBaseChangeMenuLevelMethod.Invoke(menuBase, new[] { level, false });
    }

    public static object GetMenuRoot(object menuBase)
    {
        if (menuBase.GetType() == _mainMenuType)
        {
            var realRoot = _mainMenuRealMenuRootField.GetValue(menuBase);
            if (realRoot != null) return realRoot;
        }

        return _menuBaseMenuRootField.GetValue(menuBase);
    }

    public static IList GetMenuLevelItems(object menuLevel)
    {
        return (IList)_menuLevelItemsField.GetValue(menuLevel);
    }
    
    public static void AddItemToMenuLevel(ItemTemplate template, object level, int? insertAt = null)
    {
        int itemPosition = -1;
        if (insertAt.HasValue)
        {
            var itemsCount = GetMenuLevelItems(level).Count;
            itemPosition = insertAt.Value >= 0
                ? insertAt.Value
                : itemsCount + insertAt.Value + 1;
        }
            
        object item;
        if (template.Getter != null && template.Setter != null)
        {
            item = _menuLevelAddScrollableItemMethod.Invoke(level, new object[]
            {
                template.Text, template.OnSelect, template.IsDefault, template.Getter, template.Setter, itemPosition
            });
        } 
        else
        {
            item = _menuLevelAddItemMethod.Invoke(level, new object[]
            {
                template.Text, template.OnSelect, template.IsDefault, itemPosition
            });
        }
            
        _menuItemSelectableProperty.SetValue(item, template.Selectable);
        if (template.SuffixText != null)
        {
            _menuItemSuffixTextProperty.SetValue(item, template.SuffixText);
        }
    }
    
    public static object BuildMenuLevelObject(LevelTemplate template, object existingLevel = null)
    {
        var level = existingLevel ?? Activator.CreateInstance(_menuLevelType);
        _menuLevelTitleProperty.SetValue(level, template.Title);
        _menuLevelAButtonStringProperty.SetValue(level, template.AButtonString);
        _menuLevelBButtonStringProperty.SetValue(level, template.BButtonString);
        _menuLevelTitleProperty.SetValue(level, template.Title);
        _menuLevelIsDynamicField.SetValue(level, true);
        _menuLevelOversizedField.SetValue(level, template.Oversized);
        if (template.OnReset != null)
        {
            _menuLevelOnResetField.SetValue(level, template.OnReset);
        }

        if (template.OnPostDraw != null)
        {
            _menuLevelOnPostDrawField.SetValue(level, template.OnPostDraw);
        }
        foreach (var item in template.Items)
        {
            AddItemToMenuLevel(item, level);
        }
        return level;
    }
    
    public class LevelTemplate
    {
        public string Title;
        public string AButtonString;
        public string BButtonString;
        public bool Oversized;
        public Action OnReset;
        public Action<SpriteBatch, SpriteFont, GlyphTextRenderer, float> OnPostDraw;
        public List<ItemTemplate> Items = new();

        public void AddPaddingLines(int count)
        {
            for (var i = 0; i < count; i++)
            {
                Items.Add(ItemTemplate.Empty());
            }
        }
    }
    
    public class ItemTemplate
    {
        public string Text;
        public Action OnSelect = () => { };
        public bool Selectable = true;
        public bool IsDefault = false;
        public Func<string> SuffixText;
        public Func<int> Getter;
        public Action<int, int> Setter;

        public static ItemTemplate Empty() => new()
        {
            Selectable = false
        };

        public static ItemTemplate StaticLabel(string label) => new()
        {
            Selectable = false,
            Text = label,
        };
        
        public static ItemTemplate DynamicLabel(Func<string> label) => new()
        {
            Selectable = false,
            SuffixText = label
        };
    }
}
