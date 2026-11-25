#define _GNU_SOURCE
#include <errno.h>
#include <fcntl.h>
#include <limits.h>
#include <poll.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/socket.h>
#include <sys/un.h>
#include <time.h>
#include <unistd.h>

/* Linux placement v1. All integer fields are big endian, GUIDs are RFC 4122
 * bytes, and the header is version, kind, and 16-bit body length. */
enum {
    APP_UID = 1654, HEADER = 4, CHALLENGE = 16, PROVE = 1, PLACED, GO, RELEASING,
    TUPLE = 104, PROOF = 140, CONTAINER_OFFSET = 64, NAMESPACE_OFFSET = 80,
    DEADLINE_OFFSET = 96, PID_OFFSET = 104, PARENT_OFFSET = 108, TICKS_OFFSET = 112,
    BOOT_OFFSET = 120, CONTROLLER_OFFSET = 136
};

static int refused(void)
{
    fputs("C2060_CUSTODY_REFUSED PlacementExchange\n", stderr);
    return 3;
}

static uint64_t now_ns(void)
{
    struct timespec now;
    if (clock_gettime(CLOCK_MONOTONIC, &now) != 0)
        return UINT64_MAX;
    return (uint64_t)now.tv_sec * 1000000000 + (uint64_t)now.tv_nsec;
}

static int number(const char *text, uint64_t *value)
{
    uint64_t parsed = 0;
    if (!*text)
        return 0;
    for (; *text; ++text) {
        if (*text < '0' || *text > '9' || parsed > (UINT64_MAX - (*text - '0')) / 10)
            return 0;
        parsed = parsed * 10 + (*text - '0');
    }
    *value = parsed;
    return 1;
}

static uint64_t read_be(const unsigned char *bytes, size_t length)
{
    uint64_t value = 0;
    while (length--)
        value = (value << 8) | *bytes++;
    return value;
}

static void write_be(unsigned char *bytes, size_t length, uint64_t value)
{
    while (length) {
        bytes[--length] = (unsigned char)value;
        value >>= 8;
    }
}

static int hex(char c)
{
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'a' && c <= 'f') return c - 'a' + 10;
    return -1;
}

static int guid(const char *text, unsigned char *bytes)
{
    if (strlen(text) != 36)
        return 0;
    for (int i = 0, j = 0; i < 36;) {
        if (i == 8 || i == 13 || i == 18 || i == 23) {
            if (text[i++] != '-') return 0;
        } else {
            int a = hex(text[i++]);
            int b = hex(text[i++]);
            if (a < 0 || b < 0) return 0;
            bytes[j++] = (unsigned char)((a << 4) | b);
        }
    }
    static const unsigned char empty[16];
    return memcmp(bytes, empty, sizeof empty) != 0;
}

/* Every input must reach EOF within its limit. These pseudo-files report size
 * zero even when populated. Closing and checking the deadline is independent. */
static int read_input(const char *path, char *text, size_t bound, uint64_t deadline)
{
    if (now_ns() >= deadline)
        return 0;
    int fd = open(path, O_RDONLY | O_CLOEXEC | O_NOFOLLOW);
    if (fd < 0)
        return 0;
    size_t used = 0;
    int complete = 0;
    while (used < bound && now_ns() < deadline) {
        ssize_t n = read(fd, text + used, bound - used);
        if (n > 0) {
            if (memchr(text + used, '\0', (size_t)n)) break;
            used += (size_t)n;
        } else if (n == 0) {
            text[used] = '\0';
            complete = 1;
            break;
        } else if (errno != EINTR) {
            break;
        }
    }
    close(fd);
    return complete && now_ns() < deadline;
}

static int read_guid(const char *path, char *text, unsigned char *bytes, uint64_t deadline)
{
    if (!read_input(path, text, 40, deadline) || strlen(text) != 37 || text[36] != '\n')
        return 0;
    text[36] = '\0';
    return guid(text, bytes);
}

static int self_stat(uint64_t deadline, pid_t pid, pid_t parent, uint64_t *ticks)
{
    char text[2048];
    if (!read_input("/proc/self/stat", text, sizeof text, deadline))
        return 0;
    size_t length = strlen(text);
    if (!length || text[length - 1] != '\n')
        return 0;
    char *open = strchr(text, '(');
    char *end = strrchr(text, ')');
    if (!open || open == text || open[-1] != ' ' || !end || end < open || end[1] != ' ')
        return 0;
    open[-1] = '\0';
    uint64_t observed;
    if (!number(text, &observed) || observed != (uint64_t)pid)
        return 0;
    char *cursor;
    char *field = strtok_r(end + 2, " \n", &cursor);
    if (!field || strlen(field) != 1 || !strchr("RSDTtIW", field[0]))
        return 0;
    for (int column = 4; column <= 22; ++column) {
        field = strtok_r(NULL, " \n", &cursor);
        if (!field) return 0;
        if (column == 4 && (!number(field, &observed) || observed != (uint64_t)parent))
            return 0;
        if (column == 9 && (!number(field, &observed) || (observed & 4)))
            return 0; /* PF_EXITING */
    }
    if (!number(field, ticks) || !*ticks)
        return 0;
    for (int column = 23; column <= 52; ++column)
        if (!strtok_r(NULL, " \n", &cursor)) return 0;
    return 1;
}

static int singleton(const char *controller, const char *ns, const char *id,
                     pid_t pid, uint64_t deadline)
{
    char path[160], members[32], expected[32];
    snprintf(path, sizeof path, "/sys/fs/cgroup/%s/antiphon-custody/%s/%s/tree/cgroup.procs",
             controller, ns, id);
    snprintf(expected, sizeof expected, "%d\n", pid);
    return read_input(path, members, sizeof members, deadline) && !strcmp(members, expected);
}

static int ready(int fd, short events, uint64_t deadline)
{
    struct pollfd item = { .fd = fd, .events = events };
    for (;;) {
        uint64_t now = now_ns();
        if (now >= deadline) return 0;
        uint64_t remaining_ms = (deadline - now - 1) / 1000000 + 1;
        int time_ms = remaining_ms > INT_MAX ? INT_MAX : (int)remaining_ms;
        int rc = poll(&item, 1, time_ms);
        if (rc > 0) return (item.revents & events) && now_ns() < deadline;
        if (rc < 0 && errno != EINTR) return 0;
    }
}

static int transfer(int fd, unsigned char *bytes, size_t length, int sending, uint64_t deadline)
{
    while (length && now_ns() < deadline) {
        ssize_t n = sending ? send(fd, bytes, length, MSG_NOSIGNAL) : recv(fd, bytes, length, 0);
        if (n > 0) {
            bytes += n;
            length -= (size_t)n;
        } else if (n < 0 && errno == EINTR) {
            continue;
        } else if (n < 0 && (errno == EAGAIN || errno == EWOULDBLOCK)) {
            if (!ready(fd, sending ? POLLOUT : POLLIN, deadline)) return 0;
        } else {
            return 0;
        }
    }
    return length == 0 && now_ns() < deadline;
}

static int receive_frame(int fd, unsigned char kind, unsigned char *body, size_t length,
                         uint64_t deadline)
{
    unsigned char header[HEADER];
    return transfer(fd, header, sizeof header, 0, deadline) &&
        header[0] == 1 && header[1] == kind && read_be(header + 2, 2) == length &&
        transfer(fd, body, length, 0, deadline);
}

static int send_frame(int fd, unsigned char kind, const unsigned char *body, size_t length,
                      uint64_t deadline)
{
    unsigned char frame[HEADER + PROOF] = {1, kind};
    write_be(frame + 2, 2, length);
    memcpy(frame + HEADER, body, length);
    return transfer(fd, frame, HEADER + length, 1, deadline);
}

static int connect_to_parent(int fd, const char *id, pid_t parent, uint64_t deadline)
{
    struct sockaddr_un address = { .sun_family = AF_UNIX };
    int length = snprintf(address.sun_path + 1, sizeof address.sun_path - 1,
                          "antiphon-custody-v1-%s", id);
    if (connect(fd, (struct sockaddr *)&address, sizeof(sa_family_t) + 1 + length) != 0) {
        if (errno != EINPROGRESS || !ready(fd, POLLOUT, deadline))
            return 0;
        int error;
        socklen_t size = sizeof error;
        if (getsockopt(fd, SOL_SOCKET, SO_ERROR, &error, &size) != 0 || error)
            return 0;
    }
    struct ucred peer;
    socklen_t size = sizeof peer;
    return getsockopt(fd, SOL_SOCKET, SO_PEERCRED, &peer, &size) == 0 &&
        size == sizeof peer && peer.uid == APP_UID && peer.pid == parent &&
        getppid() == parent && now_ns() < deadline;
}

static int exchange(int fd, const char *id, const unsigned char *container, uint64_t deadline)
{
    pid_t pid = getpid(), parent = getppid();
    unsigned char proof[PROOF], go[CHALLENGE], ns_id[16];
    char ns[40], boot[40];
    uint64_t ticks;
    if (parent <= 1 || !connect_to_parent(fd, id, parent, deadline) ||
        !receive_frame(fd, PROVE, proof, TUPLE, deadline) ||
        memcmp(proof + CONTAINER_OFFSET, container, 16) != 0 ||
        read_be(proof + DEADLINE_OFFSET, 8) != deadline ||
        !read_guid("/run/antiphon-custody/namespace", ns, ns_id, deadline) ||
        memcmp(proof + NAMESPACE_OFFSET, ns_id, 16) != 0 ||
        !self_stat(deadline, pid, parent, &ticks) ||
        !read_guid("/proc/sys/kernel/random/boot_id", boot, proof + BOOT_OFFSET, deadline) ||
        !singleton("pids", ns, id, pid, deadline) ||
        !singleton("freezer", ns, id, pid, deadline))
        return 0;
    memcpy(proof + NAMESPACE_OFFSET, ns_id, 16);
    write_be(proof + PID_OFFSET, 4, pid);
    write_be(proof + PARENT_OFFSET, 4, parent);
    write_be(proof + TICKS_OFFSET, 8, ticks);
    write_be(proof + CONTROLLER_OFFSET, 4, 3);
    return send_frame(fd, PLACED, proof, sizeof proof, deadline) &&
        receive_frame(fd, GO, go, sizeof go, deadline) &&
        memcmp(proof, go, sizeof go) == 0 && getppid() == parent &&
        send_frame(fd, RELEASING, go, sizeof go, deadline);
}

int main(int argc, char **argv)
{
    if (argc == 2 && !strcmp(argv[1], "--version")) {
        puts("antiphon-custody-placement-v1");
        return 0;
    }
    unsigned char container[16];
    uint64_t deadline;
    if (argc < 5 || geteuid() != 0 || !guid(argv[1], container) ||
        !number(argv[2], &deadline) || strcmp(argv[3], "--") != 0 ||
        now_ns() >= deadline)
        return refused();

    /* Prepare the fixed privilege drop before acknowledging release. */
    char **drop = calloc((size_t)argc + 3, sizeof *drop);
    if (!drop) return refused();
    drop[0] = "/usr/bin/setpriv";
    drop[1] = "--reuid=1654";
    drop[2] = "--regid=1654";
    drop[3] = "--clear-groups";
    drop[4] = "--no-new-privs";
    drop[5] = "--";
    for (int i = 4; i < argc; ++i)
        drop[i + 2] = argv[i];

    int fd = socket(AF_UNIX, SOCK_STREAM | SOCK_CLOEXEC | SOCK_NONBLOCK, 0);
    if (fd < 0) return refused();
    int released = exchange(fd, argv[1], container, deadline);
    close(fd);
    if (!released) return refused();
    execv(drop[0], drop);
    return refused();
}
