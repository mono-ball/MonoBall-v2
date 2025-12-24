#!/usr/bin/env python3
"""
Sprite Migration Script

Migrates sprites from the old manifest.json format to the new SpriteDefinition format:
- Old: Assets/Sprites/{Players|NPCs}/{category}/{name}/manifest.json + spritesheet.png
- New: Assets/Definitions/Sprites/{players|npcs}/{category}/{name}.json + Assets/Graphics/Sprites/.../name.png

Usage:
    python migrate_sprites.py <assets_path>

Example:
    python migrate_sprites.py /path/to/MonoBallFramework.Game/Assets
"""

import json
import os
import shutil
import sys
from pathlib import Path
from dataclasses import dataclass
from typing import Optional


@dataclass
class MigrationStats:
    total: int = 0
    migrated: int = 0
    skipped: int = 0
    errors: int = 0


def migrate_manifest(manifest_path: Path, assets_root: Path, stats: MigrationStats) -> bool:
    """Migrate a single manifest.json to the new format."""
    stats.total += 1

    try:
        with open(manifest_path, 'r') as f:
            manifest = json.load(f)

        # Check if already migrated (has Id and TexturePath)
        if manifest.get('Id') and manifest.get('TexturePath'):
            print(f"  [SKIP] Already migrated: {manifest_path}")
            stats.skipped += 1
            return True

        # Determine sprite type and category from directory structure
        # Old: Assets/Sprites/{Players|NPCs}/{category}/{name}/manifest.json
        rel_path = manifest_path.relative_to(assets_root / "Sprites")
        parts = list(rel_path.parts)

        if len(parts) < 3:
            print(f"  [ERROR] Unexpected path structure: {manifest_path}")
            stats.errors += 1
            return False

        sprite_type = parts[0].lower()  # "players" or "npcs"
        # Category is the second part (e.g., "elite_four", "may", "generic")
        category = parts[1]
        # Sprite name is the folder containing manifest.json (parts[-2])
        sprite_name = manifest.get('Name', parts[-2])

        # Generate new format fields
        # Format: base:sprite:{type}/{category}/{name}
        sprite_id = f"base:sprite:{sprite_type}/{category}/{sprite_name}"
        display_name = sprite_name.replace("_", " ").title()
        texture_path = f"Graphics/Sprites/{sprite_type}/{category}/{sprite_name}.png"

        # Create new manifest with merged format
        new_manifest = {
            "Id": sprite_id,
            "DisplayName": display_name,
            "Type": "Sprite",
            "TexturePath": texture_path,
            "FrameWidth": manifest.get('FrameWidth', 16),
            "FrameHeight": manifest.get('FrameHeight', 32),
            "FrameCount": manifest.get('FrameCount', 1),
            "Frames": manifest.get('Frames', []),
            "Animations": manifest.get('Animations', [])
        }

        # Create output directories (flat structure - no sprite name subdirectory)
        data_dir = assets_root / "Definitions" / "Sprites" / sprite_type / category
        graphics_dir = assets_root / "Graphics" / "Sprites" / sprite_type / category

        data_dir.mkdir(parents=True, exist_ok=True)
        graphics_dir.mkdir(parents=True, exist_ok=True)

        # Copy spritesheet to new location
        old_spritesheet = manifest_path.parent / manifest.get('SpriteSheet', 'spritesheet.png')
        new_spritesheet = graphics_dir / f"{sprite_name}.png"

        if old_spritesheet.exists():
            shutil.copy2(old_spritesheet, new_spritesheet)
            print(f"  [COPY] {old_spritesheet.name} -> {new_spritesheet}")
        else:
            print(f"  [WARN] Spritesheet not found: {old_spritesheet}")

        # Write new manifest
        new_manifest_path = data_dir / f"{sprite_name}.json"
        with open(new_manifest_path, 'w') as f:
            json.dump(new_manifest, f, indent=2)

        print(f"  [MIGRATE] {manifest_path.name} -> {new_manifest_path}")
        stats.migrated += 1
        return True

    except Exception as e:
        print(f"  [ERROR] Failed to migrate {manifest_path}: {e}")
        stats.errors += 1
        return False


def migrate_all_sprites(assets_root: Path) -> MigrationStats:
    """Migrate all sprites from old format to new format."""
    stats = MigrationStats()

    sprites_dir = assets_root / "Sprites"
    if not sprites_dir.exists():
        print(f"Sprites directory not found: {sprites_dir}")
        return stats

    # Find all manifest.json files
    manifest_files = list(sprites_dir.rglob("manifest.json"))
    print(f"Found {len(manifest_files)} manifest.json files to migrate")
    print()

    for manifest_path in manifest_files:
        migrate_manifest(manifest_path, assets_root, stats)

    return stats


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)

    assets_path = Path(sys.argv[1])
    if not assets_path.exists():
        print(f"Assets path not found: {assets_path}")
        sys.exit(1)

    print(f"Migrating sprites in: {assets_path}")
    print("=" * 60)

    stats = migrate_all_sprites(assets_path)

    print()
    print("=" * 60)
    print("Migration Summary:")
    print(f"  Total:    {stats.total}")
    print(f"  Migrated: {stats.migrated}")
    print(f"  Skipped:  {stats.skipped}")
    print(f"  Errors:   {stats.errors}")

    if stats.errors > 0:
        sys.exit(1)


if __name__ == "__main__":
    main()
