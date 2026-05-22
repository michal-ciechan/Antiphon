export interface Section {
	path: string;        // ordinal like "1", "1.2", "1.2.3"
	level: number;       // heading level 1-6
	heading: string;     // raw heading text
	body: string;        // direct body (lines before next heading of same/higher level)
	children: Section[];
	contentHash: string; // SHA-256 first 16 hex chars of body
}

// Parse markdown into a section tree by heading levels
export function parseSections(markdown: string): Section[] {
	const lines = markdown.split('\n');
	const headingRe = /^(#{1,6})\s+(.*)/;

	type RawSection = { level: number; heading: string; bodyLines: string[]; startLine: number };
	const flat: RawSection[] = [];
	let currentBodyLines: string[] = [];
	let currentSection: RawSection | null = null;

	for (const line of lines) {
		const m = headingRe.exec(line);
		if (m) {
			if (currentSection) {
				currentSection.bodyLines = currentBodyLines;
				flat.push(currentSection);
			}
			currentSection = { level: m[1].length, heading: m[2].trim(), bodyLines: [], startLine: 0 };
			currentBodyLines = [];
		} else if (currentSection) {
			currentBodyLines.push(line);
		}
	}
	if (currentSection) {
		currentSection.bodyLines = currentBodyLines;
		flat.push(currentSection);
	}

	// Build tree
	return buildTree(flat, 0, []);
}

function buildTree(
	flat: { level: number; heading: string; bodyLines: string[] }[],
	index: number,
	counters: number[],
): Section[] {
	const result: Section[] = [];
	const levelCounters: number[] = [...counters];

	let i = index;
	while (i < flat.length) {
		const item = flat[i];
		const parentLevel = counters.length > 0 ? counters.length : 0;

		if (parentLevel > 0 && item.level <= parentLevel) break;

		// Assign ordinal counter at this level
		const depth = item.level;
		while (levelCounters.length < depth) levelCounters.push(0);
		while (levelCounters.length > depth) levelCounters.pop();

		levelCounters[depth - 1] = (levelCounters[depth - 1] ?? 0) + 1;
		const path = levelCounters.slice(0, depth).join('.');

		// Collect children
		const childStartIdx = i + 1;
		let j = childStartIdx;
		while (j < flat.length && flat[j].level > depth) j++;

		const children = j > childStartIdx
			? buildTree(flat.slice(childStartIdx, j), 0, levelCounters.slice(0, depth))
			: [];

		const body = item.bodyLines.join('\n').trimEnd();
		const section: Section = {
			path,
			level: item.level,
			heading: item.heading,
			body,
			children,
			contentHash: '', // filled in async
		};
		result.push(section);
		i = j;
	}

	return result;
}

export async function hashContent(text: string): Promise<string> {
	const encoder = new TextEncoder();
	const data = encoder.encode(text);
	const hashBuffer = await crypto.subtle.digest('SHA-256', data);
	const hashArray = Array.from(new Uint8Array(hashBuffer));
	return hashArray.map(b => b.toString(16).padStart(2, '0')).join('').slice(0, 16);
}

export async function attachHashes(sections: Section[]): Promise<void> {
	for (const section of sections) {
		section.contentHash = await hashContent(section.body);
		if (section.children.length > 0) {
			await attachHashes(section.children);
		}
	}
}

export function flattenSections(sections: Section[]): Section[] {
	const result: Section[] = [];
	for (const s of sections) {
		result.push(s);
		result.push(...flattenSections(s.children));
	}
	return result;
}
