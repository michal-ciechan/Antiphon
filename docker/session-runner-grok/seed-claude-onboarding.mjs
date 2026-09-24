// CARD-0628. Pre-seed the Claude config the CLI actually reads so a fresh task under
// /work/worktrees/* does not stop on onboarding or the trust dialog.
//
// Measured in the Claude Code binary (desktop 2.1.281; the image pins 2.1.280):
//   hasCompletedOnboarding must be true or the first-run flow runs again.
//   projects[<path>].hasTrustDialogAccepted === true is the trust flag (Qke).
//   ah() maps a directory inside a git repo to its canonical git root, which for a
//   linked worktree is the main checkout (/work/repos/antiphon), not the worktree path.
//   ub() then walks parent directories up to that git root, so /work/worktrees covers a
//   directory whose walk includes it (no git root, or a root at or above that path).
//   hasCompletedProjectOnboarding and skipDangerousModePermissionPrompt are the other
//   first-run keys the same binary reads. lastOnboardingVersion is the probe-measured
//   companion of hasCompletedOnboarding; it is set only when absent.
//
// Merges into an existing object. Never reads or writes .credentials.json.
import { readFileSync, writeFileSync, mkdirSync, renameSync, unlinkSync } from "node:fs";
import { dirname, join } from "node:path";

const pinnedOnboardingVersion = "2.1.280";
const trustPaths = ["/work/repos/antiphon", "/work/worktrees"];
const dir = process.env.CLAUDE_CONFIG_DIR || "/state/claude";

const config = readObject(join(dir, ".claude.json"));
config.hasCompletedOnboarding = true;
if (!config.lastOnboardingVersion)
    config.lastOnboardingVersion = pinnedOnboardingVersion;
if (!config.theme)
    config.theme = "dark";
if (!config.projects || typeof config.projects !== "object" || Array.isArray(config.projects))
    config.projects = {};
for (const key of trustPaths) {
    const existing = config.projects[key];
    const project = existing && typeof existing === "object" && !Array.isArray(existing) ? existing : {};
    project.hasTrustDialogAccepted = true;
    project.hasCompletedProjectOnboarding = true;
    config.projects[key] = project;
}
writeAtomic(join(dir, ".claude.json"), config);

const settingsPath = join(dir, "settings.json");
const settings = readObject(settingsPath);
settings.skipDangerousModePermissionPrompt = true;
writeAtomic(settingsPath, settings);

function readObject(path) {
    let text;
    try {
        text = readFileSync(path, "utf8");
    } catch (error) {
        if (error && error.code === "ENOENT")
            return {};
        throw error;
    }
    if (!text.trim())
        return {};
    const value = JSON.parse(text);
    if (!value || typeof value !== "object" || Array.isArray(value))
        throw new Error(path + " is not a JSON object");
    return value;
}

function writeAtomic(path, value) {
    mkdirSync(dirname(path), { recursive: true });
    const tmp = path + ".antiphon-tmp";
    writeFileSync(tmp, JSON.stringify(value) + "\n");
    try {
        unlinkSync(path);
    } catch (error) {
        if (!error || error.code !== "ENOENT")
            throw error;
    }
    renameSync(tmp, path);
}
