BtSnoozDecoder
==============

Creates a Bluetooth capture file (`btsnoop.cap`) from a bugreport log from Android, containing a BT capture data.

Because the official btsnooz.py Python implementation was not working for me on Windows (trouble with character encoding), I converted it into C# and .NET [Core].

Usage
-----

`BtSnoozDecoder bugreport-log-etc.txt btsnoop.cap`

creates a `btsnoop.cap` file containing the Bluetooth capture data (viewable in e.g. Wireshark) extracted from the input bugreport file.

Attribution
-----------

Converted from the original Python implementation in AOSP, available at https://android.googlesource.com/platform/system/bt/+/master/tools/scripts/btsnooz.py, licensed under the Apache License, Version 2.0.
