using Common;
using FezEngine.Components;
using FezEngine.Services;
using FezEngine.Tools;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace HatModLoader.Source.Menu;

public class ListMenuHandler
{
    private int _selectionIndex;
    private readonly List<Texture2D> _thumbnails = new();
    
    public List<Item> Items = new();
    public Func<int> DefaultIndex;
    public Action<int> OnSelect;
    public string NoItemsText;
    public bool NoThumbnail;
    
    public MenuMediator.LevelTemplate LevelTemplate;

    public ListMenuHandler(MenuMediator.LevelTemplate template)
    {
        LevelTemplate = template;
    }
    
    public void Initialize()
    {
        _selectionIndex = DefaultIndex?.Invoke() ?? 0;
        
        LevelTemplate.OnPostDraw += DrawSelectionThumbnail;
        LevelTemplate.OnReset += OnReset;
        
        LevelTemplate.Items.Clear();

        if (Items.Count == 0 && !string.IsNullOrEmpty(NoItemsText))
        {
            LevelTemplate.Items.Add(MenuMediator.ItemTemplate.StaticLabel(NoItemsText));
            return;
        }

        if (!NoThumbnail)
        {
            InitializeThumbnails();
            LevelTemplate.AddPaddingLines(3 + Items.Count);
        }
        
        LevelTemplate.Items.Add(new MenuMediator.ItemTemplate
        {
            Getter = () => _selectionIndex,
            Setter = (_, change) =>
            {
                int count = Items.Count;
                _selectionIndex = (_selectionIndex + change + Items.Count) % count;
            },
            SuffixText = () => $"{_selectionIndex + 1} / {Items.Count}",
            OnSelect = () => OnSelect?.Invoke(_selectionIndex),
            IsDefault = true,
        });

        for (var i = 0; i < Items.Count; i++)
        {
            var labelIndex = i; // for lambda capture
            LevelTemplate.Items.Add(MenuMediator.ItemTemplate.DynamicLabel(() => GetLabelText(labelIndex)));
        }
    }
    
    private void InitializeThumbnails()
    {
        DisposeThumbnails();
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
        if (NoThumbnail)
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

    private void OnReset()
    {
        DisposeThumbnails();
    }

    private void DisposeThumbnails()
    {
        foreach (var thumbnail in _thumbnails)
        {
            thumbnail.Dispose();
        }
        _thumbnails.Clear();
    }
    
    public struct Item
    {
        public string ThumbnailPath;
        public List<string> Labels;
    }
}