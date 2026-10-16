"""Reuse checksums only while the opened file's identity and change time agree."""
import ctypes
import hashlib
import json
import os
from pathlib import Path
import sqlite3
import threading


if os.name == "nt":
    import msvcrt
    from ctypes import wintypes

    class _FileBasicInfo(ctypes.Structure):
        _fields_ = [(name, ctypes.c_longlong) for name in
                    ("CreationTime", "LastAccessTime", "LastWriteTime", "ChangeTime")]
        _fields_.append(("FileAttributes", wintypes.DWORD))

    try:
        _get_file_info = ctypes.WinDLL("kernel32", use_last_error=True).GetFileInformationByHandleEx
        _get_file_info.argtypes = (wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD)
        _get_file_info.restype = wintypes.BOOL
    except (AttributeError, OSError):
        _get_file_info = None


def _change_time(stream, stat):
    if os.name != "nt":
        return stat.st_ctime_ns
    if _get_file_info is None:
        return None
    info = _FileBasicInfo()
    handle = msvcrt.get_osfhandle(stream.fileno())
    # FileBasicInfo = 0. st_ctime on Windows is creation time, not change time.
    if not _get_file_info(handle, 0, ctypes.byref(info), ctypes.sizeof(info)):
        return None
    return info.ChangeTime or None


def _fingerprint(stream):
    stat = os.fstat(stream.fileno())
    return (stat.st_dev, stat.st_ino, stat.st_size, stat.st_mtime_ns,
            _change_time(stream, stat))


class VerificationCache:
    def __init__(self, path, check_cancel=lambda: None):
        self.check_cancel = check_cancel
        self.hits = self.misses = self.bytes_read = 0
        self._lock = threading.Lock()
        self._connection = None
        self._pending = 0
        try:
            path = Path(path)
            path.parent.mkdir(parents=True, exist_ok=True)
            self._connection = sqlite3.connect(path, timeout=.1, check_same_thread=False)
            self._connection.execute("""CREATE TABLE IF NOT EXISTS verified_files_v1 (
                path TEXT PRIMARY KEY, fingerprint TEXT NOT NULL, digest TEXT NOT NULL)""")
            self._connection.commit()
        except (OSError, sqlite3.Error):
            self._disable()

    def _disable(self):
        connection, self._connection = self._connection, None
        if connection is not None:
            try:
                connection.close()
            except sqlite3.Error:
                pass

    def _lookup(self, path, fingerprint):
        with self._lock:
            if self._connection is None:
                return None
            try:
                row = self._connection.execute(
                    "SELECT digest FROM verified_files_v1 WHERE path = ? AND fingerprint = ?",
                    (path, json.dumps(fingerprint))).fetchone()
                if row and isinstance(row[0], str) and len(row[0]) == 64:
                    if all(char in "0123456789ABCDEF" for char in row[0]):
                        return row[0]
            except sqlite3.Error:
                self._disable()
        return None

    def _store(self, path, fingerprint, digest):
        with self._lock:
            if self._connection is None:
                return
            try:
                self._connection.execute("""INSERT OR REPLACE INTO verified_files_v1
                    (path, fingerprint, digest) VALUES (?, ?, ?)""",
                    (path, json.dumps(fingerprint), digest))
                self._pending += 1
                if self._pending >= 128:
                    self._connection.commit()
                    self._pending = 0
            except sqlite3.Error:
                self._disable()

    def sha256(self, path, *, check_cancel=None):
        def cancelled():
            self.check_cancel()
            if check_cancel is not None:
                check_cancel()

        cancelled()
        key = os.path.normcase(os.path.abspath(path))
        with open(path, "rb") as stream:
            before = _fingerprint(stream)
            cacheable = bool(before[1]) and before[-1] is not None
            if cacheable:
                cached = self._lookup(key, before)
                if cached is not None and before == _fingerprint(stream):
                    cancelled()
                    with self._lock:
                        self.hits += 1
                    return cached
            with self._lock:
                self.misses += 1
            digest = hashlib.sha256()
            while True:
                cancelled()
                block = stream.read(1024 * 1024)
                if not block:
                    break
                with self._lock:
                    self.bytes_read += len(block)
                digest.update(block)
            cancelled()
            if before != _fingerprint(stream):
                raise OSError(f"File modificato durante la verifica: {path}")
            result = digest.hexdigest().upper()
            if cacheable:
                self._store(key, before, result)
            return result

    def close(self):
        with self._lock:
            if self._connection is not None:
                try:
                    self._connection.commit()
                except sqlite3.Error:
                    pass
                self._disable()

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.close()
