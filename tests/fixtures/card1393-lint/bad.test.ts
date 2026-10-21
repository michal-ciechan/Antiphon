// Scratch client test for CARD-1393. Outside client/, so eslint . does not see it.
const pattern = /^(\x1b\[D)+$/;
pattern.test("x");
