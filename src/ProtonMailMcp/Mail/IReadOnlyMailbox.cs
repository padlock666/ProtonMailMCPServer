using ProtonMailMcp.Contracts;

namespace ProtonMailMcp.Mail;

internal interface IReadOnlyMailbox
{
    Task<IReadOnlyList<FolderResult>> ListFoldersAsync(CancellationToken cancellationToken);
    Task<SearchResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken);
    Task<MessageResult> GetMessageAsync(MessageReference reference, CancellationToken cancellationToken);
}
