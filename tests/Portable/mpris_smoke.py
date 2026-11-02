"""Exercise the real playerctl binary against a disposable MPRIS service, no desktop needed."""
import json
import subprocess
import sys
import threading
import time
import dbus
import dbus.service
from dbus.mainloop.glib import DBusGMainLoop
from gi.repository import GLib

DBusGMainLoop(set_as_default=True)
bus = dbus.SessionBus()
name = dbus.service.BusName("org.mpris.MediaPlayer2.AyuGram", bus)


class Player(dbus.service.Object):
    def __init__(self):
        super().__init__(bus, "/org/mpris/MediaPlayer2")

    @dbus.service.method("org.freedesktop.DBus.Properties", in_signature="ss", out_signature="v")
    def Get(self, interface, prop):
        return self.GetAll(interface)[prop]

    @dbus.service.method("org.freedesktop.DBus.Properties", in_signature="s", out_signature="a{sv}")
    def GetAll(self, interface):
        if interface == "org.mpris.MediaPlayer2":
            return {"Identity": "AyuGram", "CanQuit": False, "CanRaise": False,
                    "HasTrackList": False, "DesktopEntry": "ayugram"}
        return {"PlaybackStatus": "Playing", "Position": dbus.Int64(65000000),
                "Rate": 1.0, "MinimumRate": 1.0, "MaximumRate": 1.0,
                "CanControl": True, "CanPlay": True, "CanPause": True,
                "CanSeek": True, "CanGoNext": True, "CanGoPrevious": True,
                "Metadata": dbus.Dictionary({
                    "mpris:trackid": dbus.ObjectPath("/test/track1"),
                    "mpris:length": dbus.Int64(167000000),
                    "xesam:title": "Not Allowed", "xesam:artist": dbus.Array(["TV Girl"], signature="s"),
                    "xesam:album": "Who Really Cares"}, signature="sv")}


player = Player()
loop = GLib.MainLoop()


def verify():
    try:
        result = subprocess.run([sys.argv[1], "--diagnose"], capture_output=True, text=True, timeout=15, check=True)
        tracks = json.loads(result.stdout)
        assert len(tracks) == 1, tracks
        track = tracks[0]
        assert track["Source"] == "AyuGram" and track["Title"] == "Not Allowed", track
        assert track["Playing"] and 65 <= track["Position"] <= 67 and track["Duration"] == 167, track
        print("PASS: real playerctl / MPRIS metadata and microsecond position", flush=True)
    except BaseException as exc:
        print("FAIL:", repr(exc), file=sys.stderr, flush=True)
        global failed
        failed = True
    finally:
        loop.quit()


failed = False
threading.Thread(target=verify, daemon=True).start()
loop.run()
sys.exit(1 if failed else 0)
