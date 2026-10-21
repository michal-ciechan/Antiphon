// CARD-1393 witness config. Not the client project config.
export default [
    {
        files: ["**/*.ts", "**/*.tsx", "**/*.js"],
        rules: {
            "no-control-regex": "error"
        }
    }
];
