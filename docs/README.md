# MonoBall Documentation

This directory contains design documentation for the MonoBall project.

## 📁 Directory Structure

### [design/](./design/)
Design documents and specifications for system architecture and features.

**Naming Convention:** All design documents follow the pattern `{system-name}-design.md` or `{system-name}-{feature}-design.md` using kebab-case (lowercase with hyphens).

**Examples:**
- `audio-system-design.md` - Audio system architecture
- `collision-system-design.md` - Collision detection system
- `scripting-system-design.md` - Scripting system architecture
- `shader-mod-system-design.md` - Shader modding system
- `map-transition-optimization-design.md` - Map transition optimizations

### [guides/](./guides/)
Development guides and how-to documentation for common tasks and best practices.

### [research/](./research/)
Technical research and reference materials for external technologies and standards.

### [features/](./features/)
Feature-specific documentation for individual game features.

### [examples/](./examples/)
Example implementations and patterns for developers.

---

## 🔗 Related Documentation

- [`.cursorrules`](../.cursorrules) - Cursor AI coding rules and standards
- [`CLAUDE.md`](../CLAUDE.md) - Claude AI configuration and workflow
- [`MonoBall.Core/Mods/README.md`](../MonoBall.Core/Mods/README.md) - Mod system documentation

---

## Documentation Guidelines

**Keep:**
- Design documents and specifications
- System architecture documentation
- Development guides
- Technical research and reference materials

**Don't Keep:**
- Intermediary fix status documents
- Change analysis documents (changes are in git)
- Implementation summaries
- Temporary status updates
- Plan/analysis/summary variants of design documents

---

## File Naming Standards

All documentation files should follow these conventions:

- **Design documents**: `{system-name}-design.md` (kebab-case)
- **Guides**: `{topic}-guide.md` or descriptive names
- **Research**: Descriptive names with kebab-case
- **Examples**: Descriptive names with kebab-case

**Examples of good naming:**
- ✅ `audio-system-design.md`
- ✅ `collision-system-design.md`
- ✅ `shader-mod-system-design.md`
- ❌ `AUDIO_SYSTEM_DESIGN.md` (wrong case)
- ❌ `audio-system-analysis.md` (analysis, not design)
- ❌ `AudioSystemDesign.md` (wrong case)
