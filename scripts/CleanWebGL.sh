#!/bin/sh
# Run via ssh stdin. Arguments are validated locally AND here before any cleanup.
set -eu
web_sub_dir=${1:?Missing web subdirectory}
build_mode=${2:-BETA}
case "$build_mode" in
    RELEASE|BETA) ;;
    *) echo 'Invalid build mode' >&2; exit 1 ;;
esac
case "$web_sub_dir" in
    ''|*[!A-Za-z0-9_-]*) echo 'Invalid web subdirectory' >&2; exit 1 ;;
esac
case "${HOME:?Missing remote HOME}" in
    /*) ;;
    *) echo 'Remote HOME must be absolute' >&2; exit 1 ;;
esac
remote_home=$(cd -- "${HOME:?}" && pwd -P) || exit 1
[ "$remote_home" != / ] && [ "$remote_home" = "${HOME%/}" ] || {
    echo 'Remote HOME must be a canonical, non-root directory without symlinks' >&2; exit 1;
}

check_path() {
    # Check every ancestor, including HOME and www, without traversing symlinks.
    checked_path=$1
    while [ "$checked_path" != / ]; do
        [ ! -L "$checked_path" ] || { echo 'Refusing cleanup through a symlink' >&2; exit 1; }
        checked_path=$(dirname -- "$checked_path")
    done
    if [ -e "$1" ]; then
        links=$(find "$1" -type l -print -quit) || exit 1
        [ -z "$links" ] || { echo 'Refusing cleanup of a tree containing symlinks' >&2; exit 1; }
    fi
}

if [ "$build_mode" = RELEASE ]; then
    check_path "${remote_home:?}/www/${web_sub_dir:?}/Build"
    check_path "${remote_home:?}/www/${web_sub_dir:?}/TemplateData"
    mkdir -p -- "${remote_home:?}/www/${web_sub_dir:?}"
    rm -rf -- "${remote_home:?}/www/${web_sub_dir:?}/Build"
    rm -rf -- "${remote_home:?}/www/${web_sub_dir:?}/TemplateData"
else
    check_path "${remote_home:?}/www/${web_sub_dir:?}/beta/Build"
    check_path "${remote_home:?}/www/${web_sub_dir:?}/beta/TemplateData"
    mkdir -p -- "${remote_home:?}/www/${web_sub_dir:?}/beta"
    rm -rf -- "${remote_home:?}/www/${web_sub_dir:?}/beta/Build"
    rm -rf -- "${remote_home:?}/www/${web_sub_dir:?}/beta/TemplateData"
fi
