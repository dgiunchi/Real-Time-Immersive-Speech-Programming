/** 
  * @file "./test.js" 
  * @desc "PRINTS: The upper memory limit for V8s HEAP" 
  * */

const maxHeapSz = require('v8').getHeapStatistics().heap_size_limit;
const maxHeapSz_GB = (maxHeapSz / 1024 ** 3).toFixed(1);

console.log('--------------------------');
console.log(`${maxHeapSz_GB}GB`);
