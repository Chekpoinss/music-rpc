# Platform roadmap

## Available

- Windows GUI/tray and optional startup entry.
- Shared source selection, catalog matching, Rich Presence payloads, and reconnect handling.
- Linux console entry point using playerctl/MPRIS and Unix Discord IPC.
- Windows and Ubuntu CI, including a synthetic MPRIS integration test.

## Next steps

- Test real AyuGram and Cider sessions across Linux distributions and desktop environments.
- Test Flatpak/Snap combinations and document session-bus/socket permissions that users may need for their particular setup.
- Add a Linux GUI/tray and optional user service packaging.
- Improve cover selection for tracks with altered tags and for embedded/local artwork, with an explicit strategy for making images accessible to Discord.
- Add release automation and signed packages where practical.

There is no native Apple Music for Linux integration in this project. Apple Music support on Linux depends on a third-party client publishing suitable MPRIS metadata. Ordinary browser sessions are ignored unless a specific session is explicitly selected.
