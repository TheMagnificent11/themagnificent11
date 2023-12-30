using Microsoft.AspNetCore.Components;

namespace TheMagnificent11.Site.Pages;

public partial class Post
{
    [Parameter]
    public string Id { get; set; } = string.Empty;

    private string Markdown { get; set; } = string.Empty;

    protected override void OnInitialized()
    {
        base.OnInitialized();

        this.Markdown = MarkDownHelper.GetMarkdownFromEmbeddedResource($"Pages.{this.Id}.md");
    }
}
