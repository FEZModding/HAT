using Common;
using FezEngine.Components;
using FezEngine.Services;
using FezEngine.Tools;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace HatModLoader.Source.Menu;

public class ListMenuHandler : IDisposable
{
    private int _selectionIndex;
    private readonly List<Texture2D> _thumbnails = new();
    private object _levelToRebuild;
    
    public List<Item> Items = new();
    public Func<int> DefaultIndex;
    public Action<int> OnSelect;
    public string NoItemsText;
    public string ScrollPrefix;
    public bool NoThumbnail;
    public bool LoopOver;
    
    public MenuMediator.LevelTemplate LevelTemplate { get; private set; }

    public ListMenuHandler(MenuMediator.LevelTemplate template)
    {
        LevelTemplate = template;
    }
    
    public void Initialize()
    {
        _selectionIndex = DefaultIndex?.Invoke() ?? 0;
        if (_selectionIndex >= Items.Count || _selectionIndex < 0)
        {
            _selectionIndex = 0;
        }
        
        LevelTemplate.OnPostDraw += DrawSelectionThumbnail;
        
        RebuildItemTemplates();
    }

    public void MarkLevelForRebuilding(object menuLevelToRebuild)
    {
        _levelToRebuild = menuLevelToRebuild;
    }

    private void RebuildItemTemplates()
    {
        LevelTemplate.Items.Clear();

        if (Items.Count == 0 && !string.IsNullOrEmpty(NoItemsText))
        {
            LevelTemplate.Items.Add(MenuMediator.ItemTemplate.StaticLabel(NoItemsText));
            return;
        }

        var maxLabelsCount = Items.Max(item => item.Labels.Count);
        
        if (!NoThumbnail)
        {
            InitializeThumbnails();
            LevelTemplate.AddPaddingLines(3 + maxLabelsCount);
        }

        LevelTemplate.Items.Add(new MenuMediator.ItemTemplate
        {
            Getter = () => _selectionIndex,
            Setter = (_, change) => ChangeSelectionIndex(change),
            SuffixText = GetScrollItemText,
            OnSelect = OnItemSelected,
            Disabled = Items[_selectionIndex].Disabled,
            IsDefault = true,
        });
        
        for (var i = 0; i < maxLabelsCount; i++)
        {
            var labelIndex = i; // for lambda capture
            var labelItem = MenuMediator.ItemTemplate.DynamicLabel(() => GetLabelText(labelIndex));
            labelItem.Disabled = Items[_selectionIndex].Disabled;
            LevelTemplate.Items.Add(labelItem);
        }
    }

    private void ChangeSelectionIndex(int change)
    {
        int count = Items.Count;
        _selectionIndex = LoopOver
            ? (_selectionIndex + change + count) % count
            : Math.Min(Math.Max(0, _selectionIndex + change), count - 1);

        if (_levelToRebuild != null)
        {
            RebuildItemTemplates();
            MenuMediator.BuildMenuLevelObject(LevelTemplate, _levelToRebuild);
        }
    }

    private string GetScrollItemText()
    {
        var scrollPrefix = !string.IsNullOrEmpty(ScrollPrefix) ? ScrollPrefix + " " : "";
        
        if (!string.IsNullOrEmpty(Items[_selectionIndex].CustomTitle))
        {
            return Items[_selectionIndex].CustomTitle;
        }
        
        var firstNonCustomIndex = Items.FindIndex(i => string.IsNullOrEmpty(i.CustomTitle));
        var displayIndex = _selectionIndex - firstNonCustomIndex + 1;
        var displayItemCount = Items.Count - firstNonCustomIndex;
        return $"{scrollPrefix}{displayIndex} / {displayItemCount}";
    }

    private void OnItemSelected()
    {
        if (!Items[_selectionIndex].Disabled)
        {
            OnSelect?.Invoke(_selectionIndex);
        }
    }
    
    private void InitializeThumbnails()
    {
        if (_thumbnails.Count > 0)
        {
            return;
        }
        
        var cm = ServiceHelper.Get<IContentManagerProvider>().Global;
        foreach (var item in Items)
        {
            Texture2D thumbnail = null;
            if (!string.IsNullOrEmpty(item.ThumbnailPath))
            {
                try
                {
                    thumbnail = cm.Load<Texture2D>(item.ThumbnailPath);
                }
                catch
                {
                    Logger.Log("ScrollableListMenuHandler", $"Unable to load thumbnail {item.ThumbnailPath}");
                }
            }
            _thumbnails.Add(thumbnail);
        }
    }

    private string GetLabelText(int labelIndex)
    {
        var labels = Items[_selectionIndex].Labels;
        if (labels == null || labelIndex < 0 || labelIndex >= labels.Count)
        {
            return string.Empty;
        }
        
        return labels[labelIndex];
    }

    private void DrawSelectionThumbnail(SpriteBatch batch, SpriteFont font, GlyphTextRenderer tr, float alpha)
    {
        if (NoThumbnail || _thumbnails.Count == 0)
        {
            return;
        }
        
        Texture2D thumbnail = _thumbnails[_selectionIndex];
        if (thumbnail == null)
        {
            return;
        }

        var viewScale = batch.GraphicsDevice.GetViewScale();
        var thummbnailSize = (int)(256f * viewScale);

        var targetRectangle = new Rectangle(
            batch.GraphicsDevice.Viewport.Width / 2,
            batch.GraphicsDevice.Viewport.Height / 2 - (int)(96f * viewScale),
            thummbnailSize,
            thummbnailSize
        );
        var origin = new Vector2(thumbnail.Width * 0.5f, thumbnail.Height * 0.5f);
        
        batch.Draw(thumbnail, targetRectangle, null, Color.White, 0f, origin, SpriteEffects.None, 0f);
    }
    
    public void Dispose()
    {
        if (_thumbnails == null)
        {
            return;
        }
        _thumbnails.Clear();
    }
    
    public struct Item
    {
        public List<string> Labels;
        public string ThumbnailPath;
        public string CustomTitle;
        public bool Disabled;
    }
}