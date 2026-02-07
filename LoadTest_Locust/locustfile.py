# locustfile.py
from locust import HttpUser, task, between
import random

class WebshopUser(HttpUser):
    wait_time = between(1, 3)

    def on_start(self):
        """Felhasználó inicializálása"""
        self.product_id = random.randint(1, 100)

    @task(10)
    def browse_products(self):
        """Termékek böngészése - közvetlen DB lekérés"""
        self.client.get("/api/products?limit=20")

    @task(10)
    def browse_products_cached(self):
        """Termékek böngészése - Redis cache-ből"""
        self.client.get("/api/cached/products?limit=20", name="/api/cached/products")

    @task(5)
    def view_product(self):
        """Egyedi termék lekérése"""
        self.client.get(f"/api/products/{self.product_id}")

    @task(3)
    def search(self):
        """Keresés - DB-heavy művelet"""
        self.client.get("/api/search?q=Elektronika")

    @task(1)
    def add_to_cart(self):
        """Kosárba tétel"""
        self.client.post("/api/cart/add", json={
            "productId": random.randint(1, 100),
            "quantity": 1
        })
