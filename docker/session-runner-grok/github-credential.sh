#!/bin/sh
# CARD-0817: git reads the password on this pipe only. No credential is stored by this helper.
case "${1-}" in
  get) ;;
  *) cat >/dev/null; exit 0 ;;
esac

protocol= host= path=
while IFS= read -r line && [ -n "$line" ]; do
  case "$line" in
    protocol=*) protocol=${line#protocol=} ;;
    host=*) host=${line#host=} ;;
    path=*) path=${line#path=} ;;
  esac
done
[ "$protocol" = https ] && [ "$host" = github.com ] || exit 0

# Match the repository identity's ASCII path rules; malformed paths fail closed.
while [ "${path#/}" != "$path" ]; do path=${path#/}; done
while [ "${path%/}" != "$path" ]; do path=${path%/}; done
case "$path" in
  ''|*[!a-zA-Z0-9_./-]*|*//*|.|..|./*|../*|*/./*|*/../*|*/.|*/..) exit 0 ;;
  */*) ;;
  *) exit 0 ;;
esac
path=$(printf '%s' "$path" | LC_ALL=C tr '[:upper:]' '[:lower:]')
case "$path" in *.git) ;; *) path=$path.git ;; esac
identity=https://github.com/$path
policy_file=${ANTIPHON_PUSH_ALLOW_LIST:-/run/antiphon/push-allow-list}
token_file=${ANTIPHON_GITHUB_TOKEN_FILE:-/run/antiphon/github-token/token}
[ -f "$policy_file" ] && [ -r "$policy_file" ] || exit 0
admitted=0
{
  while IFS= read -r entry || [ -n "$entry" ]; do
    case "$entry" in
      */) case "$identity" in "$entry"*) admitted=1 ;; esac ;;
      *) [ "$entry" != "$identity" ] || admitted=1 ;;
    esac
  done < "$policy_file"
} 2>/dev/null || exit 0
[ "$admitted" = 1 ] || exit 0
[ -f "$token_file" ] && [ -r "$token_file" ] || exit 0
token=$( { tr -d '[:space:]' < "$token_file"; } 2>/dev/null) || exit 0
if [ -n "$token" ]; then
  printf 'password=%s\n' "$token"
fi
unset token
exit 0
