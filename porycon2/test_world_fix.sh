#!/bin/bash
# Test script to verify the world coordinate fix

echo "Testing porycon world coordinate fix..."
echo "========================================"
echo ""

# Backup current world file
cp output/Worlds/hoenn.world output/Worlds/hoenn_before_test.world
echo "✓ Backed up current world file"

# Run the converter to regenerate world file
echo ""
echo "Regenerating world file with fixed code..."
python3 -m porycon --input ../pokeemerald --output output --region hoenn

# Check the coordinates in the new world file
echo ""
echo "Checking coordinates in regenerated world file:"
echo "================================================"

python3 << 'EOF'
import json

with open('output/Worlds/hoenn.world', 'r') as f:
    world = json.load(f)

maps_to_check = ['dewford_town', 'route107', 'route114', 'route115']
coords = {}

for map_entry in world['maps']:
    name = map_entry['fileName'].split('/')[-1].replace('.json', '')
    if name in maps_to_check:
        coords[name] = (map_entry['x'], map_entry['y'])
        print(f"{name:20} x={map_entry['x']:6}, y={map_entry['y']:6}")

print("")
print("Verification:")
print("=============")

# Check dewford and route107
if 'dewford_town' in coords and 'route107' in coords:
    y_diff = abs(coords['dewford_town'][1] - coords['route107'][1])
    if y_diff == 0:
        print("✅ dewford_town and route107 Y coordinates MATCH (offset=0 correct!)")
    else:
        print(f"❌ dewford_town and route107 Y differ by {y_diff} pixels (should be 0)")

# Check route114 and route115
if 'route114' in coords and 'route115' in coords:
    y_diff = abs(coords['route115'][1] - coords['route114'][1])
    expected = 320  # 40 * 8
    if y_diff == expected:
        print(f"✅ route114/route115 Y offset = {y_diff} pixels (correct! expected {expected})")
    else:
        print(f"❌ route114/route115 Y offset = {y_diff} pixels (expected {expected})")
EOF

echo ""
echo "Test complete!"
