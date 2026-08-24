"""Compatibility entry point for the unified CursorPocket brand pipeline.

Use ``python tools/make_brand_assets.py`` for new automation. This wrapper stays
so older local scripts cannot silently restore the retired geometric logo.
"""

from make_brand_assets import main


if __name__ == "__main__":
    main()
