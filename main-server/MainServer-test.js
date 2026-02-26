const express = require('express');
const Redis = require('ioredis');
const cors = require('cors');

const app = express();
const redis = new Redis(); // Connects to the Redis instance on the machine (droplet or local)

app.use(cors({
    origin: 'http://localhost:8080'
}));
app.use(express.json());

app.get('/Servers', async (req, res) => {

    try {
        // Get all room keys
        const keys = await redis.keys('room:*');
        
        if (keys.length === 0) return res.json({
            rooms: []
        });

        // Fetch all room data in one go (MGET)
        const roomData = await redis.mget(keys);
        
        // Parse and filter out any nulls (expired keys)
        const activeRooms = roomData
            .map(data => JSON.parse(data))
            .filter(room => room !== null);

        const responseObject = {
            rooms: activeRooms
        }

        res.json(responseObject);
    } catch (err) {
        res.status(500).json({ error: "Failed to fetch server list" });
    }
});

app.listen(3000, () => console.log('Main Server running on port 3000'));