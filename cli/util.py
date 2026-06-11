"""Tiny helpers shared across modules."""
import sys
from urllib.parse import urlparse


def die(msg, code=1):
    print(f"error: {msg}", file=sys.stderr)
    sys.exit(code)


def server_label(url):
    """Short host[:port] label for status output."""
    p = urlparse(url)
    return p.netloc or url
