/* Loads the published library the way any host does and calls it: the version, a render with colours, a render that
 * fails (its reason from mermaider_last_error), and memory given back. Exit 0 when all of it holds. */
#include <dlfcn.h>
#include <stdio.h>
#include <string.h>

#include "../mermaider.h"

#define CHECK(condition, message) do { if (!(condition)) { fprintf(stderr, "FAILED: %s\n", message); return 1; } } while (0)

int main(int argc, char **argv)
{
	if (argc < 2) { fprintf(stderr, "usage: smoke <path to the library>\n"); return 2; }
	void *library = dlopen(argv[1], RTLD_NOW);
	CHECK(library != NULL, dlerror());

	int (*version)(void) = dlsym(library, "mermaider_abi_version");
	char *(*render)(const char *, const char *) = dlsym(library, "mermaider_render_svg");
	const char *(*last_error)(void) = dlsym(library, "mermaider_last_error");
	void (*release)(void *) = dlsym(library, "mermaider_free");
	CHECK(version && render && last_error && release, "an export is missing");
	CHECK(version() == MERMAIDER_ABI_VERSION, "the ABI version is not the header's");

	char *svg = render("graph TD\n  A[Start] --> B{Ok?}\n  B -- yes --> C[Done]", "{\"bg\":\"#102030\",\"fg\":\"#e0e8f0\",\"accent\":\"#ff8800\",\"style\":\"tonal\"}");
	if (svg == NULL) { fprintf(stderr, "FAILED: render failed: %s\n", last_error() ? last_error() : "(no reason)"); return 1; }
	CHECK(strstr(svg, "<svg") != NULL && strstr(svg, "data-id=\"A\"") != NULL, "no diagram in the output");
	CHECK(strstr(svg, "#102030") != NULL && strstr(svg, "#ff8800") != NULL, "the colours were not used");
	release(svg);

	CHECK(render("graph TD\n  A-->B", "{\"nope\":1}") == NULL, "an unknown option was accepted");
	const char *reason = last_error();
	CHECK(reason != NULL && strstr(reason, "nope") != NULL, "no reason for the unknown option");
	CHECK(render("not a diagram at all", NULL) == NULL, "text that is no diagram rendered");
	CHECK(last_error() != NULL, "no reason for the parse failure");

	svg = render("graph LR\n  X-->Y", NULL);
	CHECK(svg != NULL, "a render after failures failed");
	release(svg);
	release(NULL);
	printf("smoke: ok\n");
	return 0;
}
