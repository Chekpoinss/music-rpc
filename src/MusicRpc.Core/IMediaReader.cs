namespace MusicRpc;

public interface IMediaReader
{
    Task<List<Track>> ReadAsync();
}
