# MonoBall Discord Server Setup Guide

This document provides a complete step-by-step guide for setting up the MonoBall Framework Discord server with Carl Bot integration.

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Server Creation](#server-creation)
3. [Channel Setup](#channel-setup)
4. [Role Creation and Permissions](#role-creation-and-permissions)
5. [Forum Channel Configuration](#forum-channel-configuration)
6. [Carl Bot Setup](#carl-bot-setup)
7. [Testing and Verification](#testing-and-verification)
8. [Maintenance and Updates](#maintenance-and-updates)

---

## Prerequisites

- Discord account
- Server creation permissions (or own the server)
- Access to Carl Bot dashboard (free at carl.gg)

---

## Server Creation

### Step 1: Create Discord Server

1. Open Discord application or web client
2. Click the **"+"** icon on the left sidebar
3. Select **"Create My Own"**
4. Choose **"For a club or community"**
5. Enter server name: **"MonoBall Framework"**
6. Select server region (closest to your location)
7. Click **"Create"**

### Step 2: Initial Server Settings

1. Right-click server name → **Server Settings**
2. Go to **Overview**:
   - Server Name: `MonoBall Framework`
   - Server Description: `Official Discord server for the MonoBall game framework - built with MonoGame and .NET 10`
   - Server Icon: Upload MonoBall logo (optional)
   - Verification Level: **Medium** (recommended)
   - Default Notification Settings: **Only @mentions**
3. Go to **Safety**:
   - Auto Moderation: Enable **Raid Protection** and **Spam Protection**
   - Verification: **Medium** level
4. Click **Save Changes**

---

## Channel Setup

### Channel Structure Overview

```
📢 announcements (Announcement Channel)
💬 general (General Chat)
👋 introductions (Introductions)
💡 support (Forum Channel)
💻 development (Development Discussion)
🎨 showcase (Project Showcase)
📚 resources (Resources & Links)
🔧 roles (Role Selection)
📜 rules (Server Rules)
🔍 mod-logs (Moderation Logs - Optional)
⭐ starboard (Starboard - Optional)
```

### Step 0: Create Categories

Before creating channels, create categories to organize them. Categories help group related channels together.

**Category Structure:**

```
📌 Important
   ├── #announcements
   ├── #roles
   ├── #rules
   └── #resources

💬 Community
   ├── #general
   ├── #introductions
   └── #showcase

💻 Development & Support
   ├── #support (Forum)
   └── #development

🔧 Moderation (Optional)
   ├── #mod-logs
   └── #starboard
```

**Rationale:**

- **Important** (4 channels): Contains essential read-only channels and server information
- **Community** (3 channels): Social channels for member interaction
- **Development & Support** (2 channels): Technical discussion and support forum grouped together
- **Moderation** (2 channels): Admin/mod tools (optional, hidden from regular members)

**How to Create Categories:**

1. Right-click server name → **Create Category**
2. Enter category name
3. Click **Create Category**
4. Repeat for all categories below

**Category 1: Important**

- Category Name: `Important`
- Permissions: Inherit from server (no special overrides needed)
- **Channels in this category:**
  - `#announcements` - Announcements only
  - `#roles` - Role selection
  - `#rules` - Server rules
  - `#resources` - Documentation and links

**Category 2: Community**

- Category Name: `Community`
- Permissions: Inherit from server
- **Channels in this category:**
  - `#general` - General discussion
  - `#introductions` - New member introductions
  - `#showcase` - Project sharing

**Category 3: Development & Support**

- Category Name: `Development & Support`
- Permissions: Inherit from server
- **Channels in this category:**
  - `#support` - Support forum (Q&A, bugs, features)
  - `#development` - Technical development discussion

**Category 4: Moderation (Optional)**

- Category Name: `Moderation`
- Permissions:
  - `@everyone`: ❌ View Channel (hide category from regular members)
  - Admin/Moderator roles: ✅ View Channel
- **Channels in this category:**
  - `#mod-logs` - Moderation action logs
  - `#starboard` - Popular messages

**Note:** When creating channels, you'll select the appropriate category from the dropdown. Categories appear as collapsible sections in the channel list.

---

### Step 1: Create Text Channels

For each channel below, follow these steps:

1. Right-click server name → **Create Channel**
2. Select **Text Channel**
3. Enter channel name
4. Click **Create Channel**
5. Configure settings (see individual channel configs below)

#### Channel 1: `#announcements`

**Channel Settings:**

- Type: **Text Channel**
- Category: **Important**
- Channel Name: `announcements`
- Topic: `Important announcements, updates, and releases`
- Slowmode: **None**
- NSFW: **Off**

**Note:** When creating this channel, select the "Important" category from the dropdown.

**Permissions Configuration:**

1. Right-click `#announcements` → **Edit Channel**
2. Go to **Permissions** tab
3. Click **Advanced permissions**
4. Configure `@everyone`:
   - ✅ View Channel
   - ❌ Send Messages
   - ❌ Create Public Threads
   - ✅ Add Reactions
   - ❌ Manage Messages
   - ❌ Mention Everyone
5. Add **Admin** role (if exists):
   - ✅ View Channel
   - ✅ Send Messages
   - ✅ Create Public Threads
   - ✅ Manage Messages
   - ✅ Mention Everyone
   - ✅ Pin Messages
6. Add **Moderator** role (if exists):
   - ✅ View Channel
   - ✅ Send Messages
   - ✅ Manage Messages
   - ✅ Mention Everyone
7. Click **Save Changes**

**Purpose:** Announcements-only channel for releases, updates, and important news.

---

#### Channel 2: `#general`

**Channel Settings:**

- Type: **Text Channel**
- Category: **Community**
- Channel Name: `general`
- Topic: `General discussion about MonoBall and game development`
- Slowmode: **0 seconds**
- NSFW: **Off**

**Note:** When creating this channel, select the "Community" category from the dropdown.

**Permissions Configuration:**

1. Right-click `#general` → **Edit Channel** → **Permissions**
2. Configure `@everyone`:
   - ✅ View Channel
   - ✅ Send Messages
   - ✅ Create Public Threads
   - ✅ Add Reactions
   - ✅ Embed Links
   - ✅ Attach Files
   - ✅ Use External Emojis
   - ✅ Read Message History
3. Click **Save Changes**

**Purpose:** General discussion channel for community chat.

---

#### Channel 3: `#introductions`

**Channel Settings:**

- Type: **Text Channel**
- Category: **Community**
- Channel Name: `introductions`
- Topic: `Introduce yourself to the community!`
- Slowmode: **0 seconds** (or use Carl Bot to limit one message per user)
- NSFW: **Off**

**Note:** When creating this channel, select the "Community" category from the dropdown.

**Permissions Configuration:**

1. Right-click `#introductions` → **Edit Channel** → **Permissions**
2. Configure `@everyone`:
   - ✅ View Channel
   - ✅ Send Messages
   - ✅ Create Public Threads
   - ✅ Add Reactions
   - ✅ Embed Links
   - ✅ Attach Files
   - ❌ Manage Messages
3. Click **Save Changes**

**Purpose:** New member introductions.

**Note:** Configure Carl Bot to limit one message per user (see Carl Bot section).

---

#### Channel 4: `#development`

**Channel Settings:**

- Type: **Text Channel**
- Category: **Development & Support**
- Channel Name: `development`
- Topic: `Technical discussions, architecture, and implementation details`
- Slowmode: **0 seconds**
- NSFW: **Off**

**Note:** When creating this channel, select the "Development & Support" category from the dropdown.

**Permissions Configuration:**

1. Right-click `#development` → **Edit Channel** → **Permissions**
2. Configure `@everyone`:
   - ✅ View Channel
   - ✅ Send Messages
   - ✅ Create Public Threads
   - ✅ Add Reactions
   - ✅ Embed Links
   - ✅ Attach Files
   - ✅ Use External Emojis
   - ✅ Read Message History
3. Click **Save Changes**

**Purpose:** Technical discussion for developers and contributors.

---

#### Channel 5: `#showcase`

**Channel Settings:**

- Type: **Text Channel**
- Category: **Community**
- Channel Name: `showcase`
- Topic: `Share your projects built with MonoBall!`
- Slowmode: **0 seconds**
- NSFW: **Off**

**Note:** When creating this channel, select the "Community" category from the dropdown.

**Permissions Configuration:**

1. Right-click `#showcase` → **Edit Channel** → **Permissions**
2. Configure `@everyone`:
   - ✅ View Channel
   - ✅ Send Messages
   - ✅ Create Public Threads
   - ✅ Add Reactions
   - ✅ Embed Links
   - ✅ Attach Files
   - ✅ Use External Emojis
   - ✅ Read Message History
3. Click **Save Changes**

**Purpose:** Community members share projects built with MonoBall.

---

#### Channel 6: `#resources`

**Channel Settings:**

- Type: **Text Channel**
- Category: **Important**
- Channel Name: `resources`
- Topic: `Documentation links, tutorials, and useful resources`
- Slowmode: **None**
- NSFW: **Off**

**Note:** When creating this channel, select the "Important" category from the dropdown.

**Permissions Configuration:**

1. Right-click `#resources` → **Edit Channel** → **Permissions**
2. Configure `@everyone`:
   - ✅ View Channel
   - ❌ Send Messages
   - ❌ Create Public Threads
   - ✅ Add Reactions
   - ✅ Read Message History
3. Add **Admin** role:
   - ✅ Send Messages
   - ✅ Manage Messages
4. Add **Moderator** role:
   - ✅ Send Messages
   - ✅ Manage Messages
5. Click **Save Changes**

**Purpose:** Read-only channel for documentation and resource links.

---

#### Channel 7: `#roles`

**Channel Settings:**

- Type: **Text Channel**
- Category: **Important**
- Channel Name: `roles`
- Topic: `React to get roles and customize your experience`
- Slowmode: **None**
- NSFW: **Off**

**Note:** When creating this channel, select the "Important" category from the dropdown.

**Permissions Configuration:**

1. Right-click `#roles` → **Edit Channel** → **Permissions**
2. Configure `@everyone`:
   - ✅ View Channel
   - ❌ Send Messages
   - ❌ Create Public Threads
   - ✅ Add Reactions
   - ✅ Read Message History
3. Add **Carl Bot** role (after inviting bot):
   - ✅ Send Messages
   - ✅ Manage Messages
   - ✅ Add Reactions
   - ✅ Embed Links
4. Click **Save Changes**

**Purpose:** Reaction role selection channel (configured via Carl Bot).

---

#### Channel 8: `#rules`

**Channel Settings:**

- Type: **Text Channel**
- Category: **Important**
- Channel Name: `rules`
- Topic: `Server rules and guidelines`
- Slowmode: **None**
- NSFW: **Off**

**Note:** When creating this channel, select the "Important" category from the dropdown.

**Permissions Configuration:**

1. Right-click `#rules` → **Edit Channel** → **Permissions**
2. Configure `@everyone`:
   - ✅ View Channel
   - ❌ Send Messages
   - ❌ Create Public Threads
   - ❌ Add Reactions
   - ✅ Read Message History
3. Add **Admin** role:
   - ✅ Send Messages
   - ✅ Manage Messages
4. Add **Moderator** role:
   - ✅ Send Messages
   - ✅ Manage Messages
5. Click **Save Changes**

**Purpose:** Server rules and guidelines (read-only for members).

**Rules Message Content:**

```
📜 MonoBall Framework Server Rules

1. **Be Respectful** - Treat all members with kindness and respect
2. **Use Appropriate Channels** - Use #support forum for questions and bug reports
3. **Search Before Posting** - Check if your question was already answered
4. **Bug Reports** - When reporting bugs, include:
   • MonoBall version
   • Steps to reproduce
   • Expected vs actual behavior
   • Screenshots if applicable
   • System information (OS, .NET version, etc.)
5. **No Spam** - No excessive messages, self-promotion, or off-topic content
6. **Follow Discord ToS** - Abide by Discord's Terms of Service

🔗 Useful Links:
• GitHub: [Your GitHub Repository URL]
• Documentation: [Your Documentation URL]
• Website: [Your Website URL]

Need help? Check out #support or ask a moderator!
```

---

#### Channel 9: `#mod-logs` (Optional)

**Channel Settings:**

- Type: **Text Channel**
- Category: **Moderation**
- Channel Name: `mod-logs`
- Topic: `Moderation action logs`
- Slowmode: **None**
- NSFW: **Off**

**Note:** When creating this channel, select the "Moderation" category from the dropdown. This category should be hidden from regular members.

**Permissions Configuration:**

1. Right-click `#mod-logs` → **Edit Channel** → **Permissions**
2. Configure `@everyone`:
   - ❌ View Channel (hide from regular members)
3. Add **Admin** role:
   - ✅ View Channel
   - ✅ Send Messages
   - ✅ Read Message History
4. Add **Moderator** role:
   - ✅ View Channel
   - ✅ Send Messages
   - ✅ Read Message History
5. Add **Carl Bot** role:
   - ✅ View Channel
   - ✅ Send Messages
   - ✅ Embed Links
6. Click **Save Changes**

**Purpose:** Log channel for moderation actions (configured via Carl Bot).

---

#### Channel 10: `#starboard` (Optional)

**Channel Settings:**

- Type: **Text Channel**
- Category: **Moderation** (or **Community** if you prefer)
- Channel Name: `starboard`
- Topic: `Popular messages from the community`
- Slowmode: **None**
- NSFW: **Off**

**Note:** When creating this channel, select the "Moderation" category from the dropdown (or "Community" if you want it visible to all members).

**Permissions Configuration:**

1. Right-click `#starboard` → **Edit Channel** → **Permissions**
2. Configure `@everyone`:
   - ✅ View Channel
   - ❌ Send Messages
   - ❌ Create Public Threads
   - ✅ Read Message History
3. Add **Carl Bot** role:
   - ✅ Send Messages
   - ✅ Embed Links
   - ✅ Manage Messages
4. Click **Save Changes**

**Purpose:** Starboard for popular messages (configured via Carl Bot).

---

### Step 2: Create Forum Channel

#### Channel: `#support` (Forum Channel)

**Channel Creation:**

1. Right-click server name → **Create Channel**
2. Select **Forum Channel**
3. Channel Name: `support`
4. Category: **Development & Support**
5. Click **Create Channel**

**Note:** When creating this channel, select the "Development & Support" category from the dropdown.

**Channel Settings:**

1. Right-click `#support` → **Edit Channel**
2. Go to **Overview**:
   - Channel Name: `support`
   - Category: **Development & Support**
   - Description: See "Forum Guidelines" below
   - Slowmode: **None**
   - NSFW: **Off**
3. Go to **Tags** tab (see Forum Tags section below)
4. Go to **Permissions** tab (see Forum Permissions section below)
5. Click **Save Changes**

**Forum Guidelines (Description):**

```
📋 Support Forum Guidelines

Please use tags when creating a post:
• [Question] - General questions about MonoBall
• [Bug Report] - Report bugs (include version, steps to reproduce)
• [Feature Request] - Suggest new features

When reporting bugs, please include:
- MonoBall version
- Steps to reproduce
- Expected vs actual behavior
- Screenshots if applicable
- System information (OS, .NET version, MonoGame version)

Search existing posts before creating a new one!
Use the search function to check if your question was already answered.
```

**Forum Tags Configuration:**

Create these tags in order:

1. **Tag: `[Question]`**

   - Name: `Question`
   - Emoji: ❓
   - Color: Blue (#3498db)
   - Mod only: ❌

2. **Tag: `[Bug Report]`**

   - Name: `Bug Report`
   - Emoji: 🐛
   - Color: Red (#e74c3c)
   - Mod only: ❌

3. **Tag: `[Feature Request]`**

   - Name: `Feature Request`
   - Emoji: 💡
   - Color: Green (#2ecc71)
   - Mod only: ❌

4. **Tag: `[Solved]`**

   - Name: `Solved`
   - Emoji: ✅
   - Color: Gray (#95a5a6)
   - Mod only: ✅

5. **Tag: `[Investigating]`**

   - Name: `Investigating`
   - Emoji: 🔍
   - Color: Orange (#f39c12)
   - Mod only: ✅

6. **Tag: `[Planned]`**

   - Name: `Planned`
   - Emoji: 📋
   - Color: Purple (#9b59b6)
   - Mod only: ✅

7. **Tag: `[Duplicate]`**
   - Name: `Duplicate`
   - Emoji: 🔄
   - Color: Gray (#7f8c8d)
   - Mod only: ✅

**How to Create Tags:**

1. Right-click `#support` → **Edit Channel**
2. Go to **Tags** tab
3. Click **Create Tag**
4. Enter tag name
5. Select emoji (click emoji icon)
6. Choose color
7. Toggle "Mod only" if needed
8. Click **Save Changes**
9. Repeat for all tags

**Forum Permissions Configuration:**

1. Right-click `#support` → **Edit Channel** → **Permissions**
2. Configure `@everyone`:
   - ✅ View Channel
   - ✅ Create Posts
   - ✅ Send Messages (in threads)
   - ✅ Create Public Threads
   - ✅ Add Reactions
   - ✅ Embed Links
   - ✅ Attach Files
   - ✅ Read Message History
   - ❌ Manage Posts
   - ❌ Manage Threads
   - ❌ Manage Tags
3. Add **Admin** role:
   - ✅ Manage Posts
   - ✅ Manage Threads
   - ✅ Manage Tags
   - ✅ Pin Messages
   - ✅ Manage Messages
4. Add **Moderator** role:
   - ✅ Manage Posts
   - ✅ Manage Threads
   - ✅ Manage Tags
   - ✅ Pin Messages
   - ✅ Manage Messages
5. Click **Save Changes**

**Purpose:** Central support hub for questions, bug reports, and feature requests.

---

## Role Creation and Permissions

### Step 1: Create Roles

For each role below, follow these steps:

1. Right-click server name → **Server Settings**
2. Go to **Roles** (left sidebar)
3. Click **Create Role**
4. Configure role settings (see individual role configs below)
5. Click **Save Changes**

**Important:** Create roles in this order (top to bottom) for proper hierarchy:

1. Admin (highest)
2. Moderator
3. Developer
4. Bug Reporter
5. Contributor
6. Announcements
7. Member (optional)
8. @everyone (lowest - always last)

#### Role 1: Admin

**Role Settings:**

- Role Name: `Admin`
- Role Color: Red (#e74c3c)
- Display separately: ✅
- Mentionable: ✅
- Hoist role: ✅

**Permissions:**

- ✅ Administrator (grants all permissions)

**Purpose:** Full server administration access.

---

#### Role 2: Moderator

**Role Settings:**

- Role Name: `Moderator`
- Role Color: Orange (#f39c12)
- Display separately: ✅
- Mentionable: ✅
- Hoist role: ✅

**Permissions:**

- ✅ View Channels
- ✅ Manage Channels
- ✅ Manage Roles (can assign roles below Moderator)
- ✅ Manage Messages
- ✅ Manage Threads
- ✅ Kick Members
- ✅ Ban Members (optional)
- ✅ Mention Everyone
- ✅ View Audit Log
- ✅ Send Messages
- ✅ Read Message History
- ✅ Add Reactions
- ✅ Embed Links
- ✅ Attach Files
- ✅ Use External Emojis
- ✅ Manage Events

**Purpose:** Moderate server, manage messages, and handle member issues.

---

#### Role 3: Developer

**Role Settings:**

- Role Name: `Developer`
- Role Color: Blue (#3498db)
- Display separately: ✅
- Mentionable: ✅
- Hoist role: ✅

**Permissions:**

- Same as `@everyone` (inherits base permissions)
- Additional channel-specific permissions:
  - `#announcements`: ✅ Send Messages

**Purpose:** Contributors and developers who can post announcements.

**Channel Override Setup:**

1. Right-click `#announcements` → **Edit Channel** → **Permissions**
2. Click **Add Role** → Select **Developer**
3. Enable:
   - ✅ View Channel
   - ✅ Send Messages
   - ✅ Manage Messages
4. Click **Save Changes**

---

#### Role 4: Bug Reporter

**Role Settings:**

- Role Name: `Bug Reporter`
- Role Color: Light Red (#e67e22)
- Display separately: ❌
- Mentionable: ❌
- Hoist role: ❌

**Permissions:**

- Same as `@everyone` (no special permissions)

**Purpose:** Role for members who help test and report bugs (assigned via reaction roles).

---

#### Role 5: Contributor

**Role Settings:**

- Role Name: `Contributor`
- Role Color: Green (#2ecc71)
- Display separately: ❌
- Mentionable: ❌
- Hoist role: ❌

**Permissions:**

- Same as `@everyone` (no special permissions)

**Purpose:** Role for community contributors (assigned via reaction roles).

---

#### Role 6: Announcements

**Role Settings:**

- Role Name: `Announcements`
- Role Color: Yellow (#f1c40f)
- Display separately: ❌
- Mentionable: ✅
- Hoist role: ❌

**Permissions:**

- Same as `@everyone` (no special permissions)

**Purpose:** Role that can be pinged for important announcements (assigned via reaction roles).

---

#### Role 7: Member (Optional)

**Role Settings:**

- Role Name: `Member`
- Role Color: Gray (#95a5a6)
- Display separately: ❌
- Mentionable: ❌
- Hoist role: ❌

**Permissions:**

- Same as `@everyone` (no special permissions)

**Purpose:** Base role assigned automatically to all members (via Carl Bot auto-roles).

---

### Step 2: Set Role Hierarchy

1. Right-click server name → **Server Settings** → **Roles**
2. Drag roles to order them (top = highest priority):
   ```
   1. Admin (top)
   2. Moderator
   3. Developer
   4. Bug Reporter
   5. Contributor
   6. Announcements
   7. Member (if created)
   8. @everyone (bottom - always last)
   ```
3. Higher roles can manage lower roles automatically
4. Click **Save Changes**

### Step 3: Assign Your Admin Role

1. Right-click your username in the member list
2. Select **Roles**
3. Check **Admin**
4. You should now have admin permissions

### Step 4: Configure @everyone Base Permissions

1. Right-click server name → **Server Settings** → **Roles**
2. Click on **@everyone**
3. Configure base permissions:

**Enable:**

- ✅ View Channels
- ✅ Send Messages
- ✅ Read Message History
- ✅ Use External Emojis
- ✅ Add Reactions
- ✅ Change Nickname
- ✅ Use Application Commands
- ✅ Use External Stickers

**Disable:**

- ❌ Administrator
- ❌ Manage Server
- ❌ Manage Channels
- ❌ Manage Roles
- ❌ Manage Messages
- ❌ Mention Everyone
- ❌ Manage Nicknames
- ❌ Kick Members
- ❌ Ban Members

4. Click **Save Changes**

---

## Carl Bot Setup

### Step 1: Invite Carl Bot

1. Go to https://carl.gg/
2. Click **"Invite Carl Bot"** button
3. Select your Discord server from the dropdown
4. Review and grant permissions:
   - ✅ Manage Messages
   - ✅ Manage Roles
   - ✅ Manage Channels
   - ✅ Send Messages
   - ✅ Embed Links
   - ✅ Read Message History
   - ✅ Add Reactions
   - ✅ Use External Emojis
   - ✅ Manage Threads
   - ✅ Manage Events
   - ✅ Use Slash Commands
5. Click **"Authorize"**
6. Complete CAPTCHA if prompted

### Step 2: Access Carl Bot Dashboard

1. Go to https://carl.gg/dashboard
2. Click **"Login with Discord"**
3. Authorize Carl Bot to access your account
4. Select your server from the server list
5. You should now see the Carl Bot dashboard

### Step 3: Configure Auto-Moderation

1. In Carl Bot dashboard, go to **Auto-Moderation**
2. Enable **Auto-Moderation**

**Spam Protection:**

- **Caps Filter**: Enable
  - Threshold: `70%` (messages with 70%+ caps are deleted)
  - Action: Delete message
- **Spam Filter**: Enable
  - Threshold: `5 messages in 5 seconds`
  - Action: Delete message
- **Mention Spam**: Enable
  - Threshold: `5 mentions in one message`
  - Action: Delete message

**Link Filtering:**

- **Enable Link Filter**: ✅
- **Action**: Delete message
- **Whitelist**: Add your domains:
  - `github.com` (or your specific repo URL)
  - `[your-docs-domain.com]`
  - `[your-website-domain.com]`
- **Allow Images**: ✅ (allow image links)

**Invite Filtering:**

- **Enable Invite Filter**: ✅
- **Action**: Delete message (or warn)
- **Whitelist**: None (or add your own Discord server invite)

**Bad Word Filter:**

- **Enable Bad Word Filter**: ✅
- **Action**: Delete message (or warn)
- **Custom Words**: Add any project-specific words to filter (optional)

**Settings:**

- **Log Channel**: Select `#mod-logs` (if created)
- **Warning Threshold**: `3 warnings = timeout`
- **Auto-delete filtered messages**: ✅
- **Delete command messages**: ✅ (optional)

3. Click **Save Changes**

---

### Step 4: Configure Reaction Roles

1. In Carl Bot dashboard, go to **Reaction Roles**
2. Click **Create Reaction Role**

**Setup for `#roles` Channel:**

**Message Content:**

```
🎮 Get Roles

React below to get roles and customize your server experience:

🎮 **Developer** - Get notified about development updates and contribute to MonoBall
🐛 **Bug Reporter** - Help test and report bugs
💡 **Contributor** - Contribute to MonoBall development
📢 **Announcements** - Get pinged for important announcements

Click the reactions below to add or remove roles!
```

**Configuration:**

- **Channel**: Select `#roles`
- **Message**: Paste the message above (or create your own)
- **Reactions**: Configure each reaction:

  **Reaction 1: 🎮**

  - Emoji: `🎮` (or use custom emoji)
  - Role: `Developer`
  - Remove reaction to remove role: ✅

  **Reaction 2: 🐛**

  - Emoji: `🐛`
  - Role: `Bug Reporter`
  - Remove reaction to remove role: ✅

  **Reaction 3: 💡**

  - Emoji: `💡`
  - Role: `Contributor`
  - Remove reaction to remove role: ✅

  **Reaction 4: 📢**

  - Emoji: `📢`
  - Role: `Announcements`
  - Remove reaction to remove role: ✅

**Settings:**

- **Require users to have a role**: ❌ (or set if you want base role requirement)
- **Remove reaction to remove role**: ✅

3. Click **Save Changes**
4. Carl Bot will post the message in `#roles` channel
5. Test by reacting to the message

---

### Step 5: Configure Auto Roles

1. In Carl Bot dashboard, go to **Auto Roles**
2. Enable **Give roles on join**

**Roles to Assign:**

- Add `Member` role (if created)
- Or leave empty if you don't want auto-assignment

**Settings:**

- **Delay**: `0 seconds` (or `5 seconds` to prevent bot spam)
- **Remove roles on leave**: ✅ (optional)

3. Click **Save Changes**

---

### Step 6: Configure Welcome Messages

1. In Carl Bot dashboard, go to **Welcome Messages**
2. Enable **Send welcome messages**

**Configuration:**

- **Channel**: Select `#introductions` or `#general`
- **Message Format**: Choose **Embed** or **Text**

**Message Content (Embed):**

```
Title: Welcome to MonoBall Framework! 🎮

Description: Welcome {user.mention}! We're glad to have you here.

Fields:
• Check out {#rules} - Server rules
• Visit {#support} - Ask questions or report bugs
• Explore {#resources} - Documentation and links
• Get roles in {#roles} - Customize your experience

Footer: MonoBall Framework - Built with MonoGame and .NET 10
```

**Message Content (Text):**

```
Welcome to the MonoBall Framework server, {user.mention}! 🎮

We're glad to have you here! Check out:
• {#rules} - Server rules
• {#support} - Ask questions or report bugs
• {#resources} - Documentation and links

Don't forget to grab some roles in {#roles}!
```

**Settings:**

- **Send as embed**: ✅ (recommended)
- **Delete welcome message after**: `5 minutes` (optional, or leave empty)
- **DM welcome message**: ❌ (or enable if preferred)

3. Click **Save Changes**

---

### Step 7: Configure Logging

1. In Carl Bot dashboard, go to **Logging**
2. Enable **Logging**

**Log Channel:**

- Select `#mod-logs` channel

**Events to Log:**

- ✅ Message Deleted
- ✅ Message Edited
- ✅ Member Joined
- ✅ Member Left
- ✅ Member Banned
- ✅ Member Unbanned
- ✅ Member Updated (nickname changes, etc.)
- ✅ Role Updates
- ✅ Channel Updates
- ✅ Server Updates

**Settings:**

- **Include images**: ✅
- **Include embeds**: ✅
- **Log bot messages**: ❌ (optional)

3. Click **Save Changes**

---

### Step 8: Configure Starboard (Optional)

1. In Carl Bot dashboard, go to **Starboard**
2. Enable **Starboard**

**Configuration:**

- **Channel**: Select `#starboard`
- **Emoji**: `⭐` (or custom emoji)
- **Minimum stars**: `3` (messages need 3+ stars to appear)
- **Self-star**: ❌ (users can't star their own messages)
- **Bot messages**: ❌ (don't star bot messages)

3. Click **Save Changes**

---

### Step 9: Configure Auto-Delete (Optional)

1. In Carl Bot dashboard, go to **Auto-Delete**
2. Enable **Auto-Delete**

**Channel-Specific Rules:**

**Rule 1: General Channel**

- **Channel**: `#general`
- **Delete messages after**: `7 days` (optional, for cleanup)
- Or leave empty to keep forever

**Rule 2: Development Channel**

- **Channel**: `#development`
- **Delete messages after**: `30 days` (optional)
- Or leave empty to keep forever

**Rule 3: Showcase Channel**

- **Channel**: `#showcase`
- **Delete messages after**: Leave empty (keep forever)

3. Click **Save Changes**

---

### Step 10: Configure Custom Commands

1. In Carl Bot dashboard, go to **Custom Commands**
2. Click **Create Command**

**Command 1: `!help`**

- **Command Name**: `help`
- **Response Type**: Text
- **Response**:

```
**MonoBall Framework Server Commands**

• `!docs` - Get documentation links
• `!github` - Get GitHub repository link
• `!bug` - Learn how to report bugs
• `!question` - Learn how to ask questions
• `!version` - Get current MonoBall version info
• `!rules` - View server rules

Need more help? Check out {#support} or ask a moderator!
```

- **Cooldown**: `5 seconds`
- Click **Save**

**Command 2: `!docs`**

- **Command Name**: `docs`
- **Response Type**: Embed (recommended) or Text
- **Response**:

```
📚 **MonoBall Documentation**

• GitHub: [Your GitHub Repository URL]
• Documentation: [Your Documentation URL]
• Getting Started: [Getting Started Guide URL]
• API Reference: [API Documentation URL]

Check out {#resources} for more links!
```

- **Cooldown**: `5 seconds`
- Click **Save**

**Command 3: `!github`**

- **Command Name**: `github`
- **Response Type**: Text
- **Response**:

```
🔗 **MonoBall GitHub Repository**

Find our GitHub repository here: [Your GitHub Repository URL]

Contributions welcome! Check out the issues and pull requests.
```

- **Cooldown**: `5 seconds`
- Click **Save**

**Command 4: `!bug`**

- **Command Name**: `bug`
- **Response Type**: Embed (recommended) or Text
- **Response**:

```
🐛 **How to Report Bugs**

1. Go to {#support} forum channel
2. Create a new post with the [Bug Report] tag
3. Include:
   • MonoBall version
   • Steps to reproduce
   • Expected vs actual behavior
   • Screenshots if applicable
   • System information (OS, .NET version, MonoGame version)

Please search existing posts first to avoid duplicates!
```

- **Cooldown**: `5 seconds`
- Click **Save**

**Command 5: `!question`**

- **Command Name**: `question`
- **Response Type**: Embed (recommended) or Text
- **Response**:

```
❓ **How to Ask Questions**

1. Search {#support} forum first - your question might already be answered!
2. Create a new post with the [Question] tag
3. Be specific and provide context
4. Include code examples if relevant
5. Check the documentation first: `!docs`

We're here to help!
```

- **Cooldown**: `5 seconds`
- Click **Save**

**Command 6: `!version`**

- **Command Name**: `version`
- **Response Type**: Embed (recommended) or Text
- **Response**:

```
📦 **MonoBall Framework**

**Current Version:** [Your Current Version]
**Framework:** MonoGame 3.8+
**.NET Version:** .NET 10.0

Check {#announcements} for the latest updates and release notes!
```

- **Cooldown**: `5 seconds`
- Click **Save**

**Command 7: `!rules`**

- **Command Name**: `rules`
- **Response Type**: Text
- **Response**:

```
📜 **Server Rules**

Check out {#rules} for the complete server rules and guidelines.

Quick summary:
• Be respectful
• Use appropriate channels
• Search before posting
• Follow Discord ToS
```

- **Cooldown**: `5 seconds`
- Click **Save**

---

### Step 11: Configure Auto-Responder (Optional)

1. In Carl Bot dashboard, go to **Auto-Responder**
2. Enable **Auto-Responder**

**Response 1:**

- **Trigger**: `monoball`
- **Response**: `MonoBall is a mod-based game framework built with MonoGame and .NET 10! Check out {#resources} for more info.`
- **Case sensitive**: ❌
- Click **Save**

**Response 2:**

- **Trigger**: `github`
- **Response**: `Find our GitHub repository here: [Your GitHub Repository URL]`
- **Case sensitive**: ❌
- Click **Save**

**Response 3:**

- **Trigger**: `docs`
- **Response**: `Documentation is available at: [Your Documentation URL]`
- **Case sensitive**: ❌
- Click **Save**

---

### Step 12: Configure Leveling (Optional)

1. In Carl Bot dashboard, go to **Leveling**
2. Enable **Leveling**

**Settings:**

- **XP per message**: `15-25` (randomized)
- **Cooldown**: `60 seconds`
- **Level up message**: ✅
- **Level up channel**: `#general` or `#announcements`

**Level Roles (Optional):**

- **Level 5**: `New Member` role (create this role if desired)
- **Level 10**: `Active Member` role (create this role if desired)
- **Level 25**: `Veteran Member` role (create this role if desired)

3. Click **Save Changes**

---

### Step 13: Configure Slowmode Manager (Optional)

1. In Carl Bot dashboard, go to **Slowmode Manager**
2. Enable **Slowmode Manager**

**Channel Rules:**

**Rule 1: Introductions**

- **Channel**: `#introductions`
- **Slowmode**: `300 seconds` (5 minutes - prevents spam)

**Rule 2: Showcase**

- **Channel**: `#showcase`
- **Slowmode**: `60 seconds` (prevents spam)

3. Click **Save Changes**

---

## Testing and Verification

### Test Checklist

#### Channel Permissions

- [ ] Test `@everyone` cannot send messages in `#announcements`
- [ ] Test `@everyone` can send messages in `#general`
- [ ] Test `@everyone` cannot send messages in `#resources`
- [ ] Test `@everyone` can create posts in `#support` forum
- [ ] Test Admin can send messages in `#announcements`
- [ ] Test Moderator can manage messages

#### Forum Channel

- [ ] Test creating a post with `[Question]` tag
- [ ] Test creating a post with `[Bug Report]` tag
- [ ] Test creating a post with `[Feature Request]` tag
- [ ] Test Moderator can add `[Solved]` tag
- [ ] Test Moderator can add `[Investigating]` tag
- [ ] Test search function works in forum
- [ ] Test forum guidelines display correctly

#### Roles

- [ ] Test Admin role has all permissions
- [ ] Test Moderator role can manage messages
- [ ] Test Developer role can post in `#announcements`
- [ ] Test role hierarchy (Admin can manage Moderator, etc.)

#### Carl Bot Features

- [ ] Test reaction roles in `#roles` channel
  - [ ] React with 🎮 → Get Developer role
  - [ ] Remove reaction → Lose Developer role
  - [ ] Test all reaction roles
- [ ] Test welcome message appears when joining
- [ ] Test auto-moderation (spam, caps, links)
- [ ] Test custom commands (`!help`, `!docs`, `!bug`, etc.)
- [ ] Test logging in `#mod-logs` (if enabled)
- [ ] Test starboard (if enabled)
- [ ] Test auto-responder (if enabled)

#### General Functionality

- [ ] Test posting in all channels
- [ ] Test thread creation
- [ ] Test file attachments
- [ ] Test embeds and links
- [ ] Test emoji reactions

### Testing Procedure

1. **Create Test Account:**

   - Use a second Discord account or ask a friend
   - Join the server with test account
   - Verify welcome message appears
   - Verify auto-role assignment (if configured)

2. **Test Permissions:**

   - Try to send messages in restricted channels
   - Verify you cannot send in `#announcements` as regular member
   - Verify you can send in `#general`

3. **Test Forum:**

   - Create a test post in `#support`
   - Add tags
   - Reply to the post
   - Search for the post

4. **Test Carl Bot:**

   - Use custom commands
   - React to role message
   - Trigger auto-moderation (carefully!)
   - Check logs

5. **Fix Issues:**
   - Document any issues found
   - Adjust permissions/settings as needed
   - Re-test until everything works

---

## Maintenance and Updates

### Regular Maintenance Tasks

**Weekly:**

- Review moderation logs
- Check for spam or rule violations
- Update pinned messages if needed
- Review and respond to forum posts

**Monthly:**

- Review server activity and engagement
- Update documentation links if needed
- Review and update custom commands
- Check Carl Bot settings and adjust if needed

**As Needed:**

- Update rules if community needs change
- Add new channels if needed
- Create new roles if needed
- Update forum tags if needed
- Update custom commands with new information

### Updating Information

**When to Update:**

- New MonoBall version released → Update `!version` command
- Documentation moved → Update `!docs` command and `#resources`
- GitHub repository changed → Update `!github` command
- New features added → Update welcome message and rules

**How to Update:**

1. Go to Carl Bot dashboard
2. Navigate to relevant section (Custom Commands, Welcome Messages, etc.)
3. Edit the content
4. Save changes
5. Test the update

### Adding New Features

**New Channel:**

1. Create channel in Discord
2. Set permissions
3. Update this document
4. Announce in `#announcements`

**New Role:**

1. Create role in Discord
2. Set permissions
3. Add to reaction roles (if applicable)
4. Update this document

**New Carl Bot Feature:**

1. Configure in Carl Bot dashboard
2. Test thoroughly
3. Document in this guide
4. Announce if it affects users

---

## Troubleshooting

### Common Issues

**Carl Bot not responding:**

- Check if bot is online (green dot in member list)
- Verify bot has proper permissions
- Check if command prefix is correct (`!` by default)
- Verify bot role is above `@everyone` in hierarchy

**Reaction roles not working:**

- Verify Carl Bot has "Manage Roles" permission
- Check role hierarchy (Carl Bot role must be above roles it assigns)
- Verify reaction role message is in correct channel
- Check if emoji is correct

**Auto-moderation too aggressive:**

- Adjust thresholds in Carl Bot dashboard
- Add whitelisted domains/users
- Review log channel for false positives

**Forum tags not showing:**

- Verify tags are created in forum channel settings
- Check if tags are mod-only (regular users can't use them)
- Verify forum channel permissions

**Permissions not working:**

- Check role hierarchy (higher roles can manage lower)
- Verify channel-specific overrides
- Check if `@everyone` permissions are blocking
- Ensure role is assigned to user

### Getting Help

- Carl Bot Support: https://carl.gg/support
- Discord Server: Check Carl Bot's official Discord
- Documentation: https://docs.carl.gg/

---

## Appendix

### Channel Summary Table

| Channel          | Category              | Type  | Purpose             | Permissions           |
| ---------------- | --------------------- | ----- | ------------------- | --------------------- |
| `#announcements` | Important             | Text  | Announcements only  | Read-only for members |
| `#general`       | Community             | Text  | General chat        | Full access           |
| `#introductions` | Community             | Text  | New member intros   | Post messages         |
| `#support`       | Development & Support | Forum | Q&A, bugs, features | Create posts, reply   |
| `#development`   | Development & Support | Text  | Dev discussion      | Full access           |
| `#showcase`      | Community             | Text  | Project sharing     | Full access           |
| `#resources`     | Important             | Text  | Documentation links | Read-only             |
| `#roles`         | Important             | Text  | Role selection      | React only            |
| `#rules`         | Important             | Text  | Server rules        | Read-only             |
| `#mod-logs`      | Moderation            | Text  | Moderation logs     | Mods only             |
| `#starboard`     | Moderation            | Text  | Popular messages    | Bot only              |

### Role Summary Table

| Role          | Color     | Permissions                       | Purpose             |
| ------------- | --------- | --------------------------------- | ------------------- |
| Admin         | Red       | Administrator                     | Full server control |
| Moderator     | Orange    | Manage messages, threads, members | Moderate server     |
| Developer     | Blue      | Base + post announcements         | Contributors        |
| Bug Reporter  | Light Red | Base                              | Testers             |
| Contributor   | Green     | Base                              | Contributors        |
| Announcements | Yellow    | Base (mentionable)                | Ping for updates    |
| Member        | Gray      | Base                              | All members         |

### Carl Bot Features Summary

| Feature          | Status         | Purpose                       |
| ---------------- | -------------- | ----------------------------- |
| Auto-Moderation  | ✅ Required    | Spam, link, invite filtering  |
| Reaction Roles   | ✅ Required    | Role assignment via reactions |
| Auto Roles       | ✅ Recommended | Assign base role on join      |
| Welcome Messages | ✅ Recommended | Greet new members             |
| Logging          | ✅ Recommended | Track moderation actions      |
| Custom Commands  | ✅ Recommended | Quick info commands           |
| Starboard        | ⚪ Optional    | Popular messages              |
| Auto-Delete      | ⚪ Optional    | Channel cleanup               |
| Auto-Responder   | ⚪ Optional    | Quick responses               |
| Leveling         | ⚪ Optional    | Member engagement             |

---

## Conclusion

This guide provides a complete setup for the MonoBall Framework Discord server. Follow each section step-by-step, test thoroughly, and maintain the server regularly.

For questions or updates to this guide, please refer to the MonoBall project documentation or contact the server administrators.

**Last Updated:** [Date]
**Version:** 1.0
